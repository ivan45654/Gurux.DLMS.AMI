﻿//
// --------------------------------------------------------------------------
//  Gurux Ltd
//
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2.
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------
using System;
using System.Data.Common;
using System.Data.SqlClient;
using Gurux.DLMS.Enums;
using Gurux.Service.Orm;
using Gurux.DLMS.AMI.Messages.DB;
using Gurux.DLMS.AMI.Messages.Rest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using System.Data.SQLite;
using Gurux.Net;
using Gurux.DLMS.AMI.Internal;
using Gurux.DLMS.AMI.Notify;
using Microsoft.Extensions.Hosting;
using Gurux.DLMS.AMI.Reader;
using System.Collections.Generic;
using Gurux.DLMS.AMI.Scheduler;

namespace Gurux.DLMS.AMI
{
    public class Startup
    {
        static readonly System.Net.Http.HttpClient httpClient = Helpers.client;

        public static string ServerAddress
        {
            get;
            set;
        }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            ServerAddress = configuration.GetSection("Client").Get<ClientOptions>().Address;
            Console.WriteLine("RestAddress: " + ServerAddress);
        }

        public IConfiguration Configuration { get; }

        private void AddSchedule(GXDbConnection connection)
        {
            List<GXSchedule> list = new List<GXSchedule>();
            GXSchedule m = new GXSchedule();
            m.Name = "Minutely";
            GXDateTime dt = new GXDateTime(DateTime.Now.Date);
            dt.Skip = DateTimeSkips.Year | DateTimeSkips.Month | DateTimeSkips.Day | DateTimeSkips.Hour | DateTimeSkips.Minute;
            m.Start = dt.ToFormatString();
            list.Add(m);
            GXSchedule h = new GXSchedule();
            h.Name = "Hourly";
            dt.Skip = DateTimeSkips.Year | DateTimeSkips.Month | DateTimeSkips.Day | DateTimeSkips.Hour;
            h.Start = dt.ToFormatString();
            list.Add(h);
            GXSchedule d = new GXSchedule();
            d.Name = "Daily";
            dt.Skip = DateTimeSkips.Year | DateTimeSkips.Month | DateTimeSkips.Day;
            d.Start = dt.ToFormatString();
            list.Add(d);
            connection.Insert(GXInsertArgs.InsertRange(list));
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
#if !NETCOREAPP2_0 && !NETCOREAPP2_1
            services.AddControllers();
#endif//!NETCOREAPP2_0 && !NETCOREAPP2_1

            if (!Configuration.GetSection("Scheduler").Get<SchedulerOptions>().Disabled)
            {
                services.AddHostedService<GXSchedulerService>();
            }

            string settings = Configuration.GetSection("Database").Get<DatabaseOptions>().Settings;
            string type = Configuration.GetSection("Database").Get<DatabaseOptions>().Type;
            Console.WriteLine("Database type: " + type);
            Console.WriteLine("Connecting: " + settings);
            DbConnection connection;
            if (string.IsNullOrEmpty(type))
            {
                //Gurux.DLMS.AMI DB is defined elsewhere.
                connection = null;
            }
            if (string.Compare(type, "Oracle", true) == 0)
            {
                connection = new OracleConnection(settings);
            }
            else if (string.Compare(type, "MSSQL", true) == 0)
            {
                connection = new SqlConnection(settings);
            }
            else if (string.Compare(type, "MySQL", true) == 0)
            {
                connection = new MySql.Data.MySqlClient.MySqlConnection(settings);
            }
            else if (string.Compare(type, "SQLite", true) == 0)
            {
                connection = new SQLiteConnection(settings);
            }
            else
            {
                throw new Exception("Invalid connection type. " + type);
            }
            if (connection != null)
            {
                connection.Open();
                GXHost h = new GXHost()
                {
                    Connection = new GXDbConnection(connection, null)
                };
                if (!h.Connection.TableExist<GXDevice>())
                {
                    Console.WriteLine("Creating tables.");
                    h.Connection.CreateTable<GXSystemError>(false, false);
                    h.Connection.CreateTable<GXDeviceTemplate>(false, false);
                    h.Connection.CreateTable<GXDevice>(false, false);
                    h.Connection.CreateTable<GXObjectTemplate>(false, false);
                    h.Connection.CreateTable<GXAttributeTemplate>(false, false);
                    h.Connection.CreateTable<GXObject>(false, false);
                    h.Connection.CreateTable<GXAttribute>(false, false);
                    h.Connection.CreateTable<GXValue>(false, false);
                    h.Connection.CreateTable<GXTask>(false, false);
                    h.Connection.CreateTable<GXError>(false, false);
                    h.Connection.CreateTable<GXSchedule>(false, false);
                    h.Connection.CreateTable<GXScheduleToAttribute>(false, false);
                    h.Connection.CreateTable<GXSchedulerInfo>(false, false);
                    h.Connection.CreateTable<GXReaderInfo>(false, false);
                    h.Connection.CreateTable<GXDeviceToReader>(false, false);
                    AddSchedule(h.Connection);
                }
                else
                {
                    h.Connection.UpdateTable<GXSystemError>();
                    h.Connection.UpdateTable<GXError>();
                    h.Connection.UpdateTable<GXReaderInfo>();
                    h.Connection.UpdateTable<GXObjectTemplate>();
                    h.Connection.UpdateTable<GXAttributeTemplate>();
                    h.Connection.UpdateTable<GXDeviceTemplate>();
                    h.Connection.UpdateTable<GXObject>();
                    h.Connection.UpdateTable<GXAttribute>();
                    h.Connection.UpdateTable<GXDevice>();
                }
                h.Connection.Insert(GXInsertArgs.Insert(new GXSystemError()
                {
                    Generation = DateTime.Now,
                    Error = "Service started: " + ServerAddress
                })); ;
                Console.WriteLine("Service started: " + ServerAddress);
                services.AddScoped<GXHost>(q =>
                {
                    return h;
                });
            }
            services.Configure<ListenerOptions>(Configuration.GetSection("Listener"));
            if (!Configuration.GetSection("Listener").Get<ListenerOptions>().Disabled)
            {
                services.AddHostedService<GXListenerService>();
            }

            services.Configure<NotifyOptions>(Configuration.GetSection("Notify"));
            NotifyOptions n = Configuration.GetSection("Notify").Get<NotifyOptions>();
            if (!n.Disabled && n.Port != 0)
            {
                services.AddHostedService<GXNotifyService>();
            }
            services.Configure<SchedulerOptions>(Configuration.GetSection("Scheduler"));
            if (!Configuration.GetSection("Scheduler").Get<SchedulerOptions>().Disabled)
            {
                services.AddHostedService<GXSchedulerService>();
            }
            services.Configure<ReaderOptions>(Configuration.GetSection("Reader"));
            ReaderOptions r = Configuration.GetSection("Reader").Get<ReaderOptions>();
            if (r.Threads != 0 && !r.Disabled)
            {
                services.AddHostedService<ReaderService>();
            }
#if NETCOREAPP2_0 || NETCOREAPP2_1
            services.AddMvc().SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Version_2_1);
#endif //NETCOREAPP2_0 || NETCOREAPP2_1
        }

#if NETCOREAPP2_0 || NETCOREAPP2_1
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, Microsoft.AspNetCore.Hosting.IHostingEnvironment env)
        {
            //Add exception handler.
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }
            app.UseMvc();
        }
#else
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
#endif //NETCOREAPP2_0 || NETCOREAPP2_1
    }
}
