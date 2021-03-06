﻿using Microsoft.Owin.Hosting;
using System;
using System.Diagnostics;

namespace Playground.Owin.Jwt
{
    class Program
    {
        static void Main(string[] args)
        {
            var endpoint = "http://127.0.0.1:3000";
            using (WebApp.Start<Startup>(endpoint))
            {
                Console.WriteLine($"Server listening on: {endpoint}");
                Process.Start(endpoint);
                Console.ReadLine();
            }
        }
    }
}
