using Skyline.DataMiner.Aspire.DllWatcher;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<DllWatcherService>();
builder.Services.AddSingleton<AutomationHostClient>();

var host = builder.Build();
host.Run();
