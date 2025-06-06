

var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("password", secret: false);
var elasticseach = builder.AddElasticsearch("elasticsearch", password);

builder.Build().Run();
