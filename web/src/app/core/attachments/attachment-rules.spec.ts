import {
  addAttachments,
  attachmentIcon,
  DEFAULT_ATTACHMENT_PROMPT,
  isImageType,
  MAX_ATTACHMENT_BYTES,
  MAX_ATTACHMENTS,
  submissionContent,
} from './attachment-rules';

const file = (name: string, size = 3, lastModified = 1): File => {
  const f = new File(['abc'], name, { lastModified });
  Object.defineProperty(f, 'size', { value: size });
  return f;
};

describe('attachment rules', () => {
  it('adds files and skips duplicates', () => {
    const a = file('a.png');
    const result = addAttachments([a], [file('a.png'), file('b.mp4')]);
    expect(result.files.map((f) => f.name)).toEqual(['a.png', 'b.mp4']);
    expect(result.rejected).toEqual([]);
  });

  it('rejects empty, too large and too many files', () => {
    const many = Array.from({ length: MAX_ATTACHMENTS }, (_, i) => file(`f${i}.txt`));
    const result = addAttachments(many, [
      file('empty.txt', 0),
      file('huge.mov', MAX_ATTACHMENT_BYTES + 1),
      file('extra.txt'),
    ]);
    expect(result.files.length).toBe(MAX_ATTACHMENTS);
    expect(result.rejected).toEqual([
      'empty.txt：空檔案',
      'huge.mov：超過 50 MB',
      'extra.txt：一次最多 10 個檔案',
    ]);
  });

  it('uses a default prompt when only files are attached', () => {
    expect(submissionContent('  hi ', [])).toBe('hi');
    expect(submissionContent('  ', [file('a.png')])).toBe(DEFAULT_ATTACHMENT_PROMPT);
    expect(submissionContent('', [])).toBe('');
  });

  it('classifies content types', () => {
    expect(isImageType('image/png')).toBe(true);
    expect(isImageType('video/mp4')).toBe(false);
    expect(isImageType(null)).toBe(false);
    expect(attachmentIcon('video/mp4')).toBe('🎬');
    expect(attachmentIcon('application/octet-stream')).toBe('📎');
  });
});
