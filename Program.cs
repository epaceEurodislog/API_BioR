// Fichier: Program.cs (version simplifiée et modulaire)
// Point d'entrée principal - Orchestration uniquement

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Services;
using DynamicsApiToDatabase.Database;
using DynamicsApiToDatabase.Models;

namespace DynamicsApiToDatabase
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== API_BIOR - Synchronisation Dynamics 365 ===");
            Console.WriteLine("Version modulaire - Débogage facilité\n");

            try
            {
                // Configuration des services
                var services = ConfigureServices();
                var serviceProvider = services.BuildServiceProvider();

                // Initialisation
                var globalStopwatch = Stopwatch.StartNew();

                // Vérification de la base de données
                var dbInitializer = serviceProvider.GetService<DatabaseInitializer>();
                if (!await dbInitializer.InitializeDatabaseAsync())
                {
                    Console.WriteLine("❌ Impossible d'initialiser la base de données");
                    return;
                }

                // Service d'authentification
                var authService = serviceProvider.GetService<AuthenticationService>();
                if (!authService.ValidateConfiguration())
                {
                    Console.WriteLine("❌ Configuration invalide");
                    return;
                }

                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    Console.WriteLine("❌ Authentification échouée");
                    return;
                }

                // Synchronisation des articles
                Console.WriteLine("\n📦 === SYNCHRONISATION DES ARTICLES ===");
                var articlesService = serviceProvider.GetService<ArticlesSyncService>();
                var articleResult = await articlesService.SyncArticlesAsync(token);
                DisplayArticlesSummary(articleResult);

                // Synchronisation des commandes
                var ordersService = serviceProvider.GetService<OrdersSyncService>();
                await ordersService.SyncAllOrdersAsync(token);

                // Résumé final
                globalStopwatch.Stop();
                Console.WriteLine($"\n🎉 === SYNCHRONISATION TERMINÉE ===");
                Console.WriteLine($"⏱️ Temps total: {globalStopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine("✅ Toutes les synchronisations sont terminées");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERREUR CRITIQUE: {ex.Message}");
                Console.WriteLine($"Détails: {ex.StackTrace}");
            }

            Console.WriteLine("\nAppuyez sur une touche pour fermer...");
            Console.ReadKey();
        }

        /// <summary>
        /// Configuration des services avec injection de dépendances
        /// </summary>
        /// <returns>Collection de services configurée</returns>
        private static IServiceCollection ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            services.AddSingleton<IConfiguration>(configuration);

            // Logging - Configuration simplifiée
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // HttpClient
            services.AddHttpClient();

            // Services métier
            services.AddScoped<AuthenticationService>();
            services.AddScoped<ArticlesSyncService>();
            services.AddScoped<OrdersSyncService>();
            services.AddScoped<DatabaseService>();
            services.AddScoped<DatabaseInitializer>();

            return services;
        }

        /// <summary>
        /// Affiche le résumé de synchronisation des articles
        /// </summary>
        /// <param name="result">Résultat de synchronisation</param>
        private static void DisplayArticlesSummary(SyncResult result)
        {
            Console.WriteLine($"\n📋 RÉSULTAT DE LA SYNCHRONISATION DES ARTICLES:");
            Console.WriteLine($"✓ Articles traités: {result.TotalProcessed}");
            Console.WriteLine($"  - Nouveaux articles ajoutés: {result.NewArticles}");
            Console.WriteLine($"  - Articles mis à jour: {result.UpdatedArticles}");
            Console.WriteLine($"  - Articles inchangés: {result.UnchangedArticles}");
            Console.WriteLine($"  - Erreurs: {result.ErrorCount}");
        }
    }
}