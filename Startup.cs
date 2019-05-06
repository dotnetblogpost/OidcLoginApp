using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OidcLoginApp.Helpers;

namespace OidcLoginApp
{
    public class Startup
    {
        protected readonly ILogger<Startup> Logger;
        protected readonly IHostingEnvironment HostingEnvironment;
        public Startup(IConfiguration configuration, IHostingEnvironment hostingEnvironment, ILogger<Startup> logger)
        {
            Configuration = configuration;
            HostingEnvironment = hostingEnvironment;
            Logger = logger;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
                 .AddCookie(options =>
                 {
                    // Cookie settings
                     options.Cookie.HttpOnly = true;
                     options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                     options.LoginPath = "/Home/Index"; //login page 
                     options.SlidingExpiration = true;
                }).AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
                {
                    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.CallbackPath = "/OidcSignin";
                    options.Authority = Configuration["authority"]; //OpenID provider authority URL
                    options.ClientId = Configuration["clientid"];
                    options.ClientSecret = Configuration["secret"];
                    options.SignedOutRedirectUri = "/Home/Login";
                    options.SaveTokens = true; //access tokens are stored in cookie. For real world scenarios, this is discouraged
                    // Configure the scope
                    options.Scope.Clear();
                    options.Scope.Add("openid"); //scopes
                    options.Scope.Add("profile");
                    options.Scope.Add("email");
                    options.ResponseType = OpenIdConnectResponseType.IdToken; //returns only id token
                    options.Events = new OpenIdConnectEvents()
                    {
                        OnAuthenticationFailed = context =>
                        {
                            context.HandleResponse();
                            context.Response.StatusCode = 500;
                            context.Response.ContentType = "text/plain";
                            if (!HostingEnvironment.IsProduction() || !HostingEnvironment.IsStaging())
                            {
                                // Debug only, in production do not share exceptions with the remote host.
                                return context.Response.WriteAsync(context.Exception.ToString());
                            }
                            return context.Response.WriteAsync(
                                "An error occurred processing your authentication. please contact administrator");
                        },
                        //Force scheme of redirect URI (THE IMPORTANT PART) -- to overcome occasional correlation errors due to mismatched redirect_uri http scheme - which can happen because load balancers handle SSL sometimes forwarded headers don't reach 
                        OnRedirectToIdentityProvider = redirectContext =>
                        {
                            Logger.LogInformation("redirecting to OpenID provider");
                            redirectContext.ProtocolMessage.RedirectUri = redirectContext.ProtocolMessage.RedirectUri.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);
                            return Task.CompletedTask;
                        },
                        //nice handler to capture any intermittent unhandled remote login failures - unable to match key errors or correlation errors
                        OnRemoteFailure = context =>
                        {
                            Logger.LogError(context.Failure.Message, context.Failure.InnerException);
                            context.HandleResponse();
                            context.HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                            context.Response.Redirect("/Home/Error");
                            return Task.CompletedTask;
                        },
                        //fired when remote SSO session is also killed
                        OnRedirectToIdentityProviderForSignOut = (context) =>
                        {
                            context.Response.Redirect(Configuration["end_session_endpoint"]); 
                            context.HandleResponse();
                            return Task.CompletedTask;
                        }
                    };
                });
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseAuthentication();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
