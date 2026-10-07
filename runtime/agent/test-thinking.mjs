// Run in the pinned Agent image. All model traffic stays on this local fake server.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createInterface } from 'node:readline';
import { randomUUID } from 'node:crypto';

const requests = [];
const server = createServer(async (req, res) => {
  let body = '';
  for await (const chunk of req) body += chunk;
  const request = JSON.parse(body);
  requests.push(request);
  res.writeHead(200, { 'Content-Type': 'text/event-stream' });
  for (const [delta, finish] of [[{ role: 'assistant', content: 'Verified.' }, null], [{}, 'stop']]) {
    res.write(`data: ${JSON.stringify({ id: 'test', object: 'chat.completion.chunk', created: 1, model: request.model, choices: [{ index: 0, delta, finish_reason: finish }] })}\n\n`);
  }
  res.end('data: [DONE]\n\n');
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const root = await mkdtemp(join(tmpdir(), 'ymir-thinking-'));
const models = ['openrouter-sonnet-5.5', 'openrouter-gpt-6.1-sol', 'openrouter-gpt-6-luna'];
await mkdir(join(root, 'sessions'));
await writeFile(join(root, 'models.json'), JSON.stringify({ providers: { ymir: {
  baseUrl: `http://127.0.0.1:${server.address().port}/v1`, api: 'openai-completions', apiKey: 'fake-key',
  models: models.map(id => ({ id, reasoning: true, compat: { supportsReasoningEffort: true } })),
} } }));

async function run(model, thinking, session) {
  const child = spawn('pi', ['--mode', 'rpc', '--provider', 'ymir', '--model', model,
    '--thinking', thinking, '--session-dir', join(root, 'sessions'), '--session-id', session,
    '-ne', '--no-skills'], {
    cwd: root, env: { ...process.env, PI_CODING_AGENT_DIR: root, PI_OFFLINE: '1', PI_TELEMETRY: '0' },
    stdio: ['pipe', 'pipe', 'pipe'],
  });
  let errors = '';
  child.stderr.on('data', chunk => errors += chunk);
  const lines = createInterface({ input: child.stdout });
  const timer = setTimeout(() => child.kill('SIGKILL'), 20000);
  let completed = false;
  child.stdin.write(JSON.stringify({ type: 'prompt', message: 'Say verified without using tools.' }) + '\n');
  try {
    for await (const line of lines) {
      const event = JSON.parse(line);
      if (event.type === 'response' && event.success === false) throw new Error(JSON.stringify(event));
      if (event.type === 'agent_settled') { completed = true; break; }
    }
    assert.ok(completed, errors || 'Pi failed to settle');
  } finally {
    clearTimeout(timer);
    child.stdin.end();
    child.kill('SIGTERM');
    lines.close();
  }
}

try {
  for (const model of models) {
    const session = randomUUID();
    // Auto follows an explicit high effort in the same session: it must reset the previous choice.
    for (const level of ['low', 'medium', 'high', 'off']) {
      const before = requests.length;
      await run(model, level, session);
      assert.equal(requests.length, before + 1);
      assert.equal(requests.at(-1).model, model);
      assert.equal(requests.at(-1).reasoning_effort, level === 'off' ? undefined : level);
      assert.equal(requests.at(-1).reasoning, undefined);
    }
    console.log(`PASS Pi RPC ${model}: low/medium/high and automatic reset in the same session`);
  }
} finally {
  server.closeAllConnections();
  await new Promise(resolve => server.close(resolve));
  await rm(root, { recursive: true, force: true });
}
