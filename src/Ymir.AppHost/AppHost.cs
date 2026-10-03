// 本機開發編排：dotnet run --project src/Ymir.AppHost
//   加上 -- --Ymir:UsePi=true 改用真正的 Pi（需先 npm i -g @earendil-works/pi-coding-agent@1.0.0）。
// 需要 container runtime 執行 SQL Server（Docker，或設定 ASPIRE_CONTAINER_RUNTIME=podman）。
// LiteLLM container 在 Sprint 4 加入；目前模型端點使用 Fake LLM。
var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddSqlServer("sql")
    .WithDataVolume("ymir-sql-data")
    .AddDatabase("ymir");

var fakeLlm = builder.AddProject<Projects.Ymir_Testing_FakeLlm>("fake-llm");

var api = builder.AddProject<Projects.Ymir_Api>("api")
    .WithReference(database)
    .WaitFor(database)
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
