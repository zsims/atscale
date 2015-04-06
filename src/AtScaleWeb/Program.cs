using Microsoft.Owin.Hosting;
using System;

namespace AtScale.Web
{
    class Program
    {
        static void Main(string[] args)
        {
            using (WebApp.Start<Startup>("http://localhost:8080/"))
            {
                Console.WriteLine("Running...");
                Console.ReadLine();
            }
        }
    }
}
