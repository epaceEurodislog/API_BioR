// Fichier: Services/ExternalProgramLauncher.cs
// Service pour lancer le programme DynamicsToXmlTranslator après la synchronisation
// VERSION MISE À JOUR pour la nouvelle structure séparée

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service pour lancer des programmes externes
    /// </summary>
    public class ExternalProgramLauncher
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ExternalProgramLauncher> _logger;

        public ExternalProgramLauncher(IConfiguration configuration, ILogger<ExternalProgramLauncher> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Lance le programme DynamicsToXmlTranslator après la synchronisation
        /// </summary>
        public async Task<bool> LaunchTranslatorAsync()
        {
            try
            {
                var exePath = GetTranslatorPath();

                var timeoutMinutes = _configuration.GetValue<int>("ExternalPrograms:TimeoutMinutes", 5);
                var timeout = TimeSpan.FromMinutes(timeoutMinutes);

                var isEnabled = _configuration.GetValue<bool>("ExternalPrograms:TranslatorEnabled", true);
                if (!isEnabled)
                {
                    _logger.LogInformation("🚫 DynamicsToXmlTranslator désactivé dans la configuration");
                    return true;
                }

                _logger.LogInformation("🚀 Lancement de DynamicsToXmlTranslator...");
                _logger.LogInformation($"📂 Chemin: {exePath}");
                _logger.LogInformation($"⏱️ Timeout: {timeout.TotalMinutes} minutes");

                if (!File.Exists(exePath))
                {
                    _logger.LogError($"❌ Le fichier DynamicsToXmlTranslator.exe n'existe pas: {exePath}");
                    return false;
                }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = Path.GetDirectoryName(exePath) // ✅ AJOUT: Définir le répertoire de travail
                };

                _logger.LogInformation("🔄 Démarrage du processus...");

                using var process = new Process { StartInfo = processStartInfo };

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _logger.LogInformation($"📤 Translator: {e.Data}");
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _logger.LogWarning($"⚠️ Translator Error: {e.Data}");
                    }
                };

                var startTime = DateTime.Now;
                bool started = process.Start();

                if (!started)
                {
                    _logger.LogError("❌ Impossible de démarrer DynamicsToXmlTranslator");
                    return false;
                }

                _logger.LogInformation($"✅ DynamicsToXmlTranslator démarré (PID: {process.Id})");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool finished = await WaitForExitAsync(process, timeout);
                var duration = DateTime.Now - startTime;

                if (finished)
                {
                    var exitCode = process.ExitCode;
                    _logger.LogInformation($"🏁 DynamicsToXmlTranslator terminé en {duration.TotalSeconds:F1}s (Code: {exitCode})");

                    if (exitCode == 0)
                    {
                        _logger.LogInformation("✅ DynamicsToXmlTranslator exécuté avec succès");
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning($"⚠️ DynamicsToXmlTranslator terminé avec le code d'erreur: {exitCode}");
                        return false;
                    }
                }
                else
                {
                    _logger.LogWarning($"⏰ Timeout dépassé ({timeout.TotalMinutes} min), arrêt forcé du processus");

                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            _logger.LogWarning("🔪 Processus arrêté de force");
                        }
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogError(killEx, "❌ Erreur lors de l'arrêt forcé du processus");
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du lancement de DynamicsToXmlTranslator");
                return false;
            }
        }

        /// <summary>
        /// Attend la fin d'un processus avec timeout asynchrone
        /// </summary>
        private async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();

                process.EnableRaisingEvents = true;
                process.Exited += (sender, e) => tcs.TrySetResult(true);

                if (process.HasExited)
                {
                    return true;
                }

                var timeoutTask = Task.Delay(timeout);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                return completedTask == tcs.Task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'attente du processus");
                return false;
            }
        }

        /// <summary>
        /// Vérifie si le programme Translator est disponible
        /// </summary>
        public bool IsTranslatorAvailable()
        {
            try
            {
                var exePath = GetTranslatorPath();

                bool exists = File.Exists(exePath);

                if (exists)
                {
                    _logger.LogInformation($"✅ DynamicsToXmlTranslator trouvé: {exePath}");
                }
                else
                {
                    _logger.LogWarning($"⚠️ DynamicsToXmlTranslator non trouvé: {exePath}");
                }

                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification de DynamicsToXmlTranslator");
                return false;
            }
        }

        /// <summary>
        /// ✅ MÉTHODE MISE À JOUR: Lance le translator avec des arguments spécifiques
        /// </summary>
        public async Task<bool> LaunchTranslatorWithArgsAsync(string args = "")
        {
            try
            {
                var exePath = GetTranslatorPath();

                var timeoutMinutes = _configuration.GetValue<int>("ExternalPrograms:TimeoutMinutes", 5);
                var timeout = TimeSpan.FromMinutes(timeoutMinutes);

                var isEnabled = _configuration.GetValue<bool>("ExternalPrograms:TranslatorEnabled", true);
                if (!isEnabled)
                {
                    _logger.LogInformation("🚫 DynamicsToXmlTranslator désactivé dans la configuration");
                    return true;
                }

                _logger.LogInformation($"🚀 Lancement de DynamicsToXmlTranslator avec filtre: {args}");
                _logger.LogInformation($"📂 Chemin: {exePath}");

                if (!File.Exists(exePath))
                {
                    _logger.LogError($"❌ Le fichier DynamicsToXmlTranslator.exe n'existe pas: {exePath}");
                    return false;
                }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args, // ← Passer l'argument (ex: "purchase")
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = Path.GetDirectoryName(exePath) // ✅ AJOUT: Répertoire de travail
                };

                using var process = new Process { StartInfo = processStartInfo };

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _logger.LogInformation($"📤 Translator: {e.Data}");
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _logger.LogWarning($"⚠️ Translator Error: {e.Data}");
                    }
                };

                var startTime = DateTime.Now;
                bool started = process.Start();

                if (!started)
                {
                    _logger.LogError("❌ Impossible de démarrer DynamicsToXmlTranslator");
                    return false;
                }

                _logger.LogInformation($"✅ DynamicsToXmlTranslator démarré avec filtre '{args}' (PID: {process.Id})");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool finished = await WaitForExitAsync(process, timeout);
                var duration = DateTime.Now - startTime;

                if (finished)
                {
                    var exitCode = process.ExitCode;
                    _logger.LogInformation($"🏁 DynamicsToXmlTranslator terminé en {duration.TotalSeconds:F1}s (Code: {exitCode})");
                    return exitCode == 0;
                }
                else
                {
                    _logger.LogWarning($"⏰ Timeout dépassé ({timeout.TotalMinutes} min)");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du lancement de DynamicsToXmlTranslator");
                return false;
            }
        }

        /// <summary>
        /// ✅ NOUVELLE MÉTHODE: Lance le translator avec un filtre spécifique (méthode simplifiée)
        /// </summary>
        public async Task<bool> LaunchTranslatorForTypeAsync(string filterType)
        {
            _logger.LogInformation($"📋 Lancement du Translator en mode filtré : {filterType}");
            return await LaunchTranslatorWithArgsAsync(filterType);
        }

        /// <summary>
        /// Obtient le chemin du Translator avec détection automatique intelligente
        /// </summary>
        private string GetTranslatorPath()
        {
            // 1. Vérifier dans la configuration
            var configPath = _configuration["ExternalPrograms:TranslatorPath"];
            if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            {
                _logger.LogInformation($"📂 Utilisation du chemin configuré: {configPath}");
                return configPath;
            }

            // 2. Chercher dans les emplacements standard relatifs au répertoire courant
            var baseDir = Directory.GetCurrentDirectory();
            var possiblePaths = new[]
            {
                // Relatif au projet actuel
                Path.Combine(baseDir, "..", "exe", "Translator", "DynamicsToXmlTranslator.exe"),
                Path.Combine(baseDir, "..", "..", "exe", "Translator", "DynamicsToXmlTranslator.exe"),
                // Dans le même répertoire
                Path.Combine(baseDir, "DynamicsToXmlTranslator.exe"),
                // Sous-dossier Translator
                Path.Combine(baseDir, "Translator", "DynamicsToXmlTranslator.exe")
            };

            foreach (var path in possiblePaths)
            {
                var normalizedPath = Path.GetFullPath(path);
                if (File.Exists(normalizedPath))
                {
                    _logger.LogInformation($"✅ Translator trouvé automatiquement: {normalizedPath}");
                    return normalizedPath;
                }
            }

            // 3. Si toujours pas trouvé, retourner un chemin par défaut et logger l'erreur
            var defaultPath = Path.Combine(baseDir, "DynamicsToXmlTranslator.exe");
            _logger.LogWarning($"⚠️ Translator non trouvé. Chemin par défaut utilisé: {defaultPath}");
            _logger.LogWarning("💡 Configurez 'ExternalPrograms:TranslatorPath' dans appsettings.json pour éviter ce message");
            return defaultPath;
        }
    }
}