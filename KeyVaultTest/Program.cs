using KeyVaultTest.Extensions;
using KeyVaultTest.Settings;

var builder = WebApplication.CreateBuilder(args);


builder.ConfigureAzureKeyVault();

builder.Services.Configure<UserSetting>(builder.Configuration.GetSection(nameof(UserSetting)));


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
