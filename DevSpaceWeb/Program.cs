using DevSpaceWeb.Data;
using DevSpaceWeb.Database;
using DevSpaceWeb.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace DevSpaceWeb;
public class Program
{
    public static InteractiveServerRenderMode RenderMode = new InteractiveServerRenderMode(prerender: false);
    /// <summary>
    /// Current directory of the running program
    /// </summary>
    public static DirectoryStructureMain Directory;

    public static HttpClient Http = new HttpClient();

    public static IServiceCollection Services;

    /// <summary>
    /// Program is running in development
    /// </summary>
    public static bool IsDevMode { get; private set; }

    public static bool IsPreviewMode { get; set; } = true;

    public static void Main(string[] args)
    {
        if (!_Data.LoadConfig())
            throw new Exception("Failed to load config file.");

        Console.WriteLine("Loaded config in: " + Program.Directory.Path);

        FileWatcher.Start();

        var builder = WebApplication.CreateBuilder(args);
        IsDevMode = builder.Environment.IsDevelopment();

        _DB.Client = new MongoDB.Driver.MongoClient(_Data.Config.Database.GetConnectionString());
        _ = Task.Run(async () =>
        {
            await _DB.StartAsync();

            while (!_DB.IsConnected)
            {

            }


        });

        // Add services to the container.
        ServiceBuilder.Build(builder, builder.Services);

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.All;
        });

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddControllers();

        Services = builder.Services;

        var app = builder.Build();


        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseForwardedHeaders();

        // Use both wwwroot and public folder for website access :)
        app.UseStaticFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(
           Path.Combine(Program.Directory.Public.Path)),
            RequestPath = "/public"
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseSession();
        app.UseAntiforgery();

        if (_Data.Config.Instance.Features.SwaggerEnabled)
        {
            Console.WriteLine("Swagger Enabled");
            app.UseSwagger(c =>
            {
            });

            if (_Data.Config.Instance.Features.SwaggerUIEnabled)
            {
                Console.WriteLine("Swagger UI Enabled");
                app.UseSwaggerUI(c =>
                {
                    c.DocumentTitle = "Dev Space Endpoints";
                    c.SwaggerEndpoint("v1/swagger.json", "Dev Space API");
                    c.SupportedSubmitMethods(SubmitMethod.Get, SubmitMethod.Post);
                    c.ConfigObject = new ConfigObject
                    {
                        //DefaultModelExpandDepth = 2,
                        DefaultModelRendering = ModelRendering.Example,
                        DefaultModelsExpandDepth = -1,
                        DisplayOperationId = false
                    };
                    //c.EnableFilter();
                });
            }

            app.MapSwagger();
        }
        //new InfoTest().Run();

        app.MapRazorComponents<Components.App>()
            .AddInteractiveServerRenderMode();
        app.MapControllers();

        app.Run();
    }
}
