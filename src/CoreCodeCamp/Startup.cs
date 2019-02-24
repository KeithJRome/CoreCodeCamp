﻿using System;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using CoreCodeCamp.Data;
using CoreCodeCamp.Data.Entities;
using CoreCodeCamp.Models;
using CoreCodeCamp.Models.Admin;
using CoreCodeCamp.Services;
using Loggly;
using Loggly.Config;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace CoreCodeCamp
{
  public class Startup
  {
    private const string IGNORE_STATUS_CODE_PAGES = "IgnoreStatusCodePages";
    IHostingEnvironment _env;

    public Startup(IHostingEnvironment env)
    {
      _env = env;
    }


    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection svcs)
    {
      if (_env.IsProduction())
      {
        svcs.AddScoped<IMailService, SendGridMailService>();
      }
      else
      {
        svcs.AddScoped<IMailService, DebugMailService>();
      }

      // Add framework services.
      svcs.AddDbContext<CodeCampContext>();
      svcs.AddScoped<ICodeCampRepository, CodeCampRepository>();
      svcs.AddTransient<CodeCampSeeder>();
      svcs.AddTransient<ViewRenderer>();

      svcs.AddTransient<IImageStorageService, ImageStorageService>();

      svcs.AddAutoMapper();

      // Configure Identity (Security)
      svcs.AddIdentity<CodeCampUser, IdentityRole>(config =>
      {
        // If you change this, you need to change the regular expression in the Vue code too!
        config.Password.RequiredLength = 8;
        config.Password.RequireDigit = true;
        config.Password.RequireLowercase = true;
        config.Password.RequireUppercase = true;
        config.Password.RequireNonAlphanumeric = false;
        config.User.RequireUniqueEmail = true;
        config.User.RequireUniqueEmail = true;
        config.SignIn.RequireConfirmedEmail = true;
        config.Lockout.MaxFailedAccessAttempts = 10;
      })
          .AddEntityFrameworkStores<CodeCampContext>()
          .AddDefaultTokenProviders();

      svcs.AddMvc(opt =>
      {
        if (_env.IsProduction())
        {
          opt.Filters.Add(new RequireHttpsAttribute());
        }
      }).SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app,
      ILoggerFactory loggerFactory,
      IConfiguration config,
      IApplicationLifetime appLifetime,
      ILogger<Startup> logger)
    {
      loggerFactory.AddConsole(config.GetSection("Logging"));

      if (_env.IsDevelopment() || config["SiteSettings:ShowErrors"].ToLower() == "true")
      {
        app.UseDeveloperExceptionPage();
        app.UseDatabaseErrorPage();
        app.UseStatusCodePages();
      }

      if (_env.IsProduction())
      {
        app.UseStatusCodePages(new StatusCodePagesOptions()
        {
          HandleAsync = ctx =>
          {
            // Ignore if from Static Files
            if (ctx.HttpContext.Response.StatusCode != 404 &&
            ctx.HttpContext.Items.ContainsKey(IGNORE_STATUS_CODE_PAGES) &&
            ((bool)ctx.HttpContext.Items[IGNORE_STATUS_CODE_PAGES]))
            {
              logger.LogInformation($"Ignoring File Not Found from Static Files: {ctx.HttpContext.Request.Path}");
            }
            else
            {
              ctx.HttpContext.Response.Redirect("/error/404");
            }

            return ctx.Next.Invoke(ctx.HttpContext);
          }
        });

        app.UseExceptionHandler("/Error/Exception");

        SetupLoggerly(loggerFactory, appLifetime, config);

      }

      app.UseStaticFiles(new StaticFileOptions()
      {
        OnPrepareResponse = ctx =>
        {
          if (ctx.Context.Response.StatusCode == 404)
          {
            // Mark this as ignore
            ctx.Context.Items.Add(IGNORE_STATUS_CODE_PAGES, true);
          }
        }
      });

      if (_env.IsDevelopment())
      {
        // For dev, just use Node Modules
        app.UseNodeModules(_env);
      }

      app.UseAuthentication();

      app.UseMvc(CreateRoutes);

    }

    private void SetupLoggerly(ILoggerFactory loggerFactory, IApplicationLifetime appLifetime, IConfiguration config)
    {
      if (config["Loggerly:Enabled"].ToLower() != "true")
      {
        return;
      }

      var logConfig = LogglyConfig.Instance;

      // Setup Basics
      logConfig.CustomerToken = config["Loggerly:Token"];
      logConfig.ApplicationName = config["Loggerly:AppName"];

      // Setup Host
      logConfig.Transport.EndpointHostname = config["Loggerly:EndpointHostname"];
      logConfig.Transport.EndpointPort = 443;
      logConfig.Transport.LogTransport = LogTransport.Https;

      // Add Tag
      var ct = new ApplicationNameTag();
      ct.Formatter = "application-{0}";
      logConfig.TagConfig.Tags.Add(ct);

      // Setup Level to Log
      var logLevel = _env.IsProduction() ? LogEventLevel.Warning : LogEventLevel.Debug;

      // Setup Serilog
      Log.Logger = new LoggerConfiguration()
      .Enrich.FromLogContext()
      .WriteTo.Loggly(logLevel)
      .CreateLogger();

      // Add Serilog
      loggerFactory.AddSerilog();

      // Ensure that log is flushed
      appLifetime.ApplicationStopped.Register(Log.CloseAndFlush);
    }

    void CreateRoutes(IRouteBuilder routes)
    {
      routes.MapRoute(
        name: "Events",
        template: string.Concat("{moniker}/{controller=Root}/{action=Index}/{id?}")
        );

      routes.MapRoute(
        name: "Default",
        template: "{controller=Root}/{action=Index}/{id?}"
        );

    }
  }
}
