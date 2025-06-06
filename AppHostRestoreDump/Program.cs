using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using static System.Runtime.InteropServices.JavaScript.JSType;

var builder = DistributedApplication.CreateBuilder(args);


var backupSource = builder.Configuration.GetValue<string>("bkp-src");
var backupDestination = builder.Configuration.GetValue<string>("bkp-dst");

builder.AddContainer("dump-restore", "elasticdump/elasticsearch-dump")
    .WithBindMount("", "")
    .WithArgs([
        $"--input={backupSource}",
        $"--output{backupDestination}",
        "--fsCompress",
        "--limit=10000",
        "--type=data"
        ]);



builder.Build().Run();
