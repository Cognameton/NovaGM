using System;
using System.Reflection;

namespace NovaGM.Services
{
    public static class VersionBanner
    {
        public static void Print()
        {
            string ver(Assembly a) => a.GetName().Version?.ToString() ?? "unknown";

            try
            {
                var avaloniaAsm = typeof(Avalonia.Application).Assembly;
                var llamaAsm    = typeof(LLama.LLamaWeights).Assembly;
                var aspnetAsm   = typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly;
                var sqliteAsm   = typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly;

                Console.WriteLine("[NovaGM] Versions:");
                Console.WriteLine($"  .NET:           {Environment.Version}");
                Console.WriteLine($"  Avalonia:       {ver(avaloniaAsm)} ({avaloniaAsm.GetName().Name})");
                Console.WriteLine($"  LLamaSharp:     {ver(llamaAsm)} ({llamaAsm.GetName().Name})");
                Console.WriteLine($"  ASP.NET Core:   {ver(aspnetAsm)} ({aspnetAsm.GetName().Name})");
                Console.WriteLine($"  Sqlite:         {ver(sqliteAsm)} ({sqliteAsm.GetName().Name})");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[NovaGM] Version banner error: " + ex.Message);
            }
        }
    }
}
