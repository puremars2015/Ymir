// 本機開發編排：dotnet run --project src/Ymir.AppHost
//   加上 -- --Ymir:UsePi=true 改用真正的 Pi（需先 npm i -g @earendil-works/pi-coding-agent@1.0.0）。
// Sprint 0：Fake LLM + API + Angular，不需要 container runtime。
// Sprint 1 起加入 SQL Server（Aspire.Hosting.SqlServer）與 LiteLLM container。
var builder = DistributedApplication.CreateBuilder(args);

var fakeLlm = builder.AddProject<Projects.Ymir_Testing_FakeLlm>("fake-llm");

var api = builder.AddProject<Projects.Ymir_Api>("api")
    .WithEnvironment("VibeMaker__Pi__ModelBaseUrl", ReferenceExpression.Create($"{fakeLlm.GetEndpoint("http")}/v1"))
    .WithEnvironment("VibeMaker__Harness", string.Equals(builder.Configuration["Ymir:UsePi"], "true", StringComparison.OrdinalIgnoreCase) ? "Pi" : "Scripted")
    .WaitFor(fakeLlm);

// ng serve 固定使用 4200（angular.json），proxy.conf.mjs 透過 services__api__http__0 找到 API。
builder.AddJavaScriptApp("web", "../../web", "start")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithExternalHttpEndpoints();

builder.Build().Run();
