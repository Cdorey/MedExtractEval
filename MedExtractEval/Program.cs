using MedExtractEval.Components;
using MedExtractEval.Components.Account;
using MedExtractEval.Data;
using MedExtractEval.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MedExtractEval
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<IdentityUserAccessor>();
            builder.Services.AddScoped<IdentityRedirectManager>();
            builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

            builder.Services.AddAuthentication(options =>
                {
                    options.DefaultScheme = IdentityConstants.ApplicationScheme;
                    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
                })
                .AddIdentityCookies();

            var identityConn = builder.Configuration.GetConnectionString("IdentityConnection")
                ?? throw new InvalidOperationException("Missing IdentityConnection.");

            var medEvalConn = builder.Configuration.GetConnectionString("MedEvalConnection")
                ?? throw new InvalidOperationException("Missing MedEvalConnection.");

            // Identity
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(identityConn));

            // 业务库（建议 factory）
            builder.Services.AddPooledDbContextFactory<MedEvalDbContext>(options =>
                options.UseSqlServer(medEvalConn));

            //builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
            builder.Services.AddScoped<IAnnotationAppService, AnnotationAppService>();

            var app = builder.Build();
            if (app.Environment.IsDevelopment())
            {
                using var scope = app.Services.CreateScope();

                // Identity DB
                var identityDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                identityDb.Database.Migrate();

                // MedEval DB (factory)
                var medFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MedEvalDbContext>>();
                await using var medDb = await medFactory.CreateDbContextAsync();
                await medDb.Database.MigrateAsync();
            }

            app.UseExceptionHandler("/Error");

            app.UseStaticFiles();
            app.UseAntiforgery();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            // Add additional endpoints required by the Identity /Account Razor components.
            app.MapAdditionalIdentityEndpoints();

            app.Run();
        }
    }
}
