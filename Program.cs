using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace DynamicsApiToDatabase
{
    class Program
    {
        private static ILogger<Program> _logger;
        private static IConfiguration _configuration;
        private static HttpClient _httpClient;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Synchronisation intelligente des articles Dynamics avec analyse des balises ===");

            // Configuration
            SetupConfiguration();
            SetupLogging();
            SetupHttpClient();

            _logger.LogInformation("Démarrage de la synchronisation des articles avec analyse des balises");

            // Création de la base de données et des tables si nécessaire
            if (!CreateDatabaseIfNotExists())
            {
                _logger.LogError("Impossible de créer ou d'accéder à la base de données. Arrêt du programme.");
                Console.WriteLine("Erreur: Problème de base de données. Vérifiez votre configuration WAMP.");
                return;
            }

            // Obtention du token d'accès
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Impossible d'obtenir un token d'accès. Arrêt du programme.");
                Console.WriteLine("Erreur: Impossible de s'authentifier auprès de l'API Dynamics.");
                return;
            }

            Console.WriteLine("✓ Authentification réussie");

            // Endpoint pour les articles
            string articlesEndpoint = "data/BRINT34ReleasedProducts";

            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation($"Début de la synchronisation depuis: {articlesEndpoint}");
            Console.WriteLine($"Récupération des articles depuis l'API...");

            // Récupération et synchronisation intelligente des articles
            var syncResult = await FetchAndSyncArticlesAsync(token, articlesEndpoint);

            stopwatch.Stop();

            Console.WriteLine($"\n=== RÉSULTAT DE LA SYNCHRONISATION ===");
            Console.WriteLine($"✓ Articles traités: {syncResult.TotalProcessed}");
            Console.WriteLine($"  - Nouveaux articles ajoutés: {syncResult.NewArticles}");
            Console.WriteLine($"  - Articles mis à jour: {syncResult.UpdatedArticles}");
            Console.WriteLine($"  - Articles inchangés: {syncResult.UnchangedArticles}");
            Console.WriteLine($"  - Erreurs: {syncResult.ErrorCount}");
            Console.WriteLine($"⏱️ Temps d'exécution: {stopwatch.ElapsedMilliseconds}ms");

            _logger.LogInformation($"Synchronisation terminée: {syncResult.TotalProcessed} articles traités en {stopwatch.ElapsedMilliseconds}ms");

            Console.WriteLine("\nAppuyez sur une touche pour fermer...");
            Console.ReadKey();
        }

        private static void SetupConfiguration()
        {
            _configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
        }

        private static void SetupLogging()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .AddFile("logs/dynamics_sync.log");
            });

            _logger = loggerFactory.CreateLogger<Program>();
        }

        private static void SetupHttpClient()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        private static async Task<string> GetAccessTokenAsync()
        {
            try
            {
                var authUrl = $"https://login.microsoftonline.com/{_configuration["TenantId"]}/oauth2/token";

                _logger.LogInformation($"Demande de token à: {authUrl}");

                var formParams = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", _configuration["ClientId"]),
                    new KeyValuePair<string, string>("client_secret", _configuration["ClientSecret"]),
                    new KeyValuePair<string, string>("resource", _configuration["Resource"])
                };

                var content = new FormUrlEncodedContent(formParams);
                var response = await _httpClient.PostAsync(authUrl, content);
                var responseText = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Réponse d'authentification: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Erreur d'authentification: {responseText}");
                    return null;
                }

                var tokenData = JsonSerializer.Deserialize<TokenResponse>(responseText);

                if (string.IsNullOrEmpty(tokenData?.access_token))
                {
                    _logger.LogError("Token d'accès vide dans la réponse");
                    return null;
                }

                _logger.LogInformation("Token d'accès obtenu avec succès");
                return tokenData.access_token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'obtention du token d'accès");
                return null;
            }
        }

        private static async Task<SyncResult> FetchAndSyncArticlesAsync(string token, string endpoint)
        {
            var result = new SyncResult();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var url = $"{_configuration["Resource"]}{endpoint}";
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                _logger.LogInformation($"Appel API GET: {url}");
                Console.WriteLine($"Appel API: {url}");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur API {response.StatusCode}: {errorContent}");
                    Console.WriteLine($"Erreur API: {response.StatusCode}");
                    LogSyncError(endpoint, $"Erreur API: {response.StatusCode}", stopwatch.ElapsedMilliseconds);
                    return result;
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"JSON reçu: {jsonContent.Length} caractères");
                Console.WriteLine($"✓ Données reçues: {jsonContent.Length} caractères");

                var jsonDocument = JsonDocument.Parse(jsonContent);

                if (!jsonDocument.RootElement.TryGetProperty("value", out var articlesArray))
                {
                    _logger.LogWarning("Propriété 'value' non trouvée dans la réponse JSON");
                    Console.WriteLine("Avertissement: Aucun article trouvé dans la réponse");
                    return result;
                }

                var articles = articlesArray.EnumerateArray().ToArray();
                Console.WriteLine($"✓ {articles.Length} articles trouvés dans l'API");

                // Synchronisation intelligente des articles
                result = await SyncArticlesWithDatabaseAsync(articles, endpoint);

                stopwatch.Stop();

                string status = result.ErrorCount == 0 ? "SUCCESS" : (result.ErrorCount < result.TotalProcessed ? "WARNING" : "ERROR");
                LogSyncResult(endpoint, status, result, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération depuis {endpoint}");
                Console.WriteLine($"Erreur: {ex.Message}");
                LogSyncError(endpoint, ex.Message, stopwatch.ElapsedMilliseconds);
                return result;
            }
        }

        private static async Task<SyncResult> SyncArticlesWithDatabaseAsync(JsonElement[] articles, string endpoint)
        {
            var result = new SyncResult();

            try
            {
                var connectionString = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"],
                    Database = _configuration["Database:Name"]
                }.ConnectionString;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    Console.WriteLine("🔄 Début de la synchronisation intelligente...");

                    // ÉTAPE 1 : Récupérer les hash existants pour comparaison
                    Console.WriteLine("📋 Récupération des articles existants...");
                    var existingHashes = await GetExistingArticleHashesAsync(connection);
                    Console.WriteLine($"✓ {existingHashes.Count} articles existants trouvés");

                    // ÉTAPE 2 : Récupérer tous les ItemIds existants pour détecter les suppressions
                    var existingItemIds = await GetExistingArticleIdsAsync(connection);

                    // ÉTAPE 3 : Traquer les ItemIds de l'API pour détecter les articles supprimés
                    var apiItemIds = new HashSet<string>();

                    // ÉTAPE 4 : Synchronisation article par article
                    Console.WriteLine("🔍 Analyse et synchronisation des articles...");

                    foreach (var article in articles)
                    {
                        try
                        {
                            result.TotalProcessed++;

                            // Extraction de l'ItemId
                            string itemId = article.TryGetProperty("ItemId", out var itemIdProp)
                                ? itemIdProp.GetString() ?? "UNKNOWN"
                                : "UNKNOWN";

                            apiItemIds.Add(itemId);

                            string articleJson = article.GetRawText();
                            string currentHash = CalculateHash(articleJson);

                            // Vérifier si l'article existe déjà
                            if (existingHashes.ContainsKey(itemId))
                            {
                                // Article existant - vérifier si modifié
                                if (existingHashes[itemId] != currentHash)
                                {
                                    // Article modifié - mettre à jour
                                    await UpdateExistingArticleAsync(connection, itemId, articleJson, currentHash, endpoint);
                                    result.UpdatedArticles++;

                                    if (result.UpdatedArticles % 10 == 0)
                                    {
                                        Console.WriteLine($"🔄 {result.UpdatedArticles} articles mis à jour");
                                    }
                                }
                                else
                                {
                                    // Article inchangé - juste mettre à jour la date de dernière vérification
                                    await TouchArticleAsync(connection, itemId);
                                    result.UnchangedArticles++;
                                }
                            }
                            else
                            {
                                // Nouvel article - insérer
                                await InsertNewArticleAsync(connection, itemId, articleJson, currentHash, endpoint);
                                result.NewArticles++;

                                if (result.NewArticles % 10 == 0)
                                {
                                    Console.WriteLine($"➕ {result.NewArticles} nouveaux articles ajoutés");
                                }
                            }

                            // Affichage du progrès global
                            if (result.TotalProcessed % 100 == 0)
                            {
                                string progressMessage = $"📊 Traités: {result.TotalProcessed}/{articles.Length} | Nouveaux: {result.NewArticles} | Modifiés: {result.UpdatedArticles} | Inchangés: {result.UnchangedArticles}";
                                Console.Write($"\r{progressMessage}");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.ErrorCount++;
                            string errorItemId = "UNKNOWN";
                            if (article.TryGetProperty("ItemId", out var idProp))
                            {
                                errorItemId = idProp.GetString() ?? "UNKNOWN";
                            }
                            _logger.LogError(ex, $"Erreur lors du traitement de l'article {result.TotalProcessed} (ItemId: {errorItemId})");
                        }
                    }

                    Console.WriteLine(); // Nouvelle ligne après le compteur

                    // ÉTAPE 5 : Détecter et marquer les articles supprimés de l'API
                    var deletedItemIds = existingItemIds.Except(apiItemIds).ToList();
                    if (deletedItemIds.Any())
                    {
                        Console.WriteLine($"🗑️ Détection de {deletedItemIds.Count} articles supprimés de l'API");

                        foreach (var deletedItemId in deletedItemIds)
                        {
                            await MarkArticleAsDeletedAsync(connection, deletedItemId);
                        }

                        Console.WriteLine($"✓ {deletedItemIds.Count} articles marqués comme supprimés");
                    }

                    // ÉTAPE 6 : Analyser et mettre à jour les balises
                    Console.WriteLine("\n🔍 Analyse des balises des articles...");
                    var detectedTags = await AnalyzeAndUpdateArticleTagsAsync(articles);
                    Console.WriteLine($"✓ Analyse des balises terminée: {detectedTags.Count} balises gérées");

                    // ÉTAPE 7 : Résumé de la synchronisation
                    Console.WriteLine($"\n📋 RÉSUMÉ DE LA SYNCHRONISATION:");
                    Console.WriteLine($"  ➕ Nouveaux articles: {result.NewArticles}");
                    Console.WriteLine($"  🔄 Articles mis à jour: {result.UpdatedArticles}");
                    Console.WriteLine($"  ✅ Articles inchangés: {result.UnchangedArticles}");
                    Console.WriteLine($"  🗑️ Articles supprimés: {deletedItemIds.Count}");
                    Console.WriteLine($"  ❌ Erreurs: {result.ErrorCount}");
                }

                _logger.LogInformation($"Synchronisation intelligente terminée: {result.NewArticles} nouveaux, {result.UpdatedArticles} modifiés, {result.UnchangedArticles} inchangés, {result.ErrorCount} erreurs");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation avec la base de données");
                throw;
            }
        }

        private static async Task<Dictionary<string, ArticleTagInfo>> AnalyzeAndUpdateArticleTagsAsync(JsonElement[] articles)
        {
            var detectedTags = new Dictionary<string, ArticleTagInfo>();

            Console.WriteLine("🔍 Analyse des balises des articles...");

            // Analyser tous les articles pour détecter les balises
            foreach (var article in articles)
            {
                AnalyzeJsonElement(article, "", detectedTags);
            }

            Console.WriteLine($"✓ {detectedTags.Count} balises détectées au total");

            // Mettre à jour la base de données avec les balises
            await UpdateArticleTagsInDatabaseAsync(detectedTags);

            return detectedTags;
        }

        private static void AnalyzeJsonElement(JsonElement element, string prefix, Dictionary<string, ArticleTagInfo> tags)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        string fullPath = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

                        // Tronquer le nom de balise si trop long pour MySQL
                        if (fullPath.Length > 190)
                        {
                            fullPath = fullPath.Substring(0, 187) + "...";
                        }

                        // Ajouter ou mettre à jour la balise
                        if (!tags.ContainsKey(fullPath))
                        {
                            tags[fullPath] = new ArticleTagInfo
                            {
                                TagName = fullPath,
                                DataType = GetJsonValueType(property.Value),
                                FirstSeen = DateTime.Now,
                                LastSeen = DateTime.Now,
                                OccurrenceCount = 1,
                                SampleValue = GetSampleValue(property.Value)
                            };
                        }
                        else
                        {
                            tags[fullPath].LastSeen = DateTime.Now;
                            tags[fullPath].OccurrenceCount++;

                            // Mettre à jour le type si nécessaire
                            string currentType = GetJsonValueType(property.Value);
                            if (tags[fullPath].DataType != currentType && currentType != "Null")
                            {
                                tags[fullPath].DataType = currentType;
                                tags[fullPath].SampleValue = GetSampleValue(property.Value);
                            }
                        }

                        // Analyser récursivement les objets imbriqués
                        if (property.Value.ValueKind == JsonValueKind.Object)
                        {
                            AnalyzeJsonElement(property.Value, fullPath, tags);
                        }
                        else if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            var arrayElements = property.Value.EnumerateArray().ToArray();
                            if (arrayElements.Length > 0)
                            {
                                AnalyzeJsonElement(arrayElements[0], fullPath, tags);
                            }
                        }
                    }
                    break;
            }
        }

        private static string GetJsonValueType(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => "String",
                JsonValueKind.Number => "Number",
                JsonValueKind.True or JsonValueKind.False => "Boolean",
                JsonValueKind.Array => "Array",
                JsonValueKind.Object => "Object",
                JsonValueKind.Null => "Null",
                _ => "Unknown"
            };
        }

        private static string GetSampleValue(JsonElement element)
        {
            try
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString()?.Substring(0, Math.Min(50, element.GetString()?.Length ?? 0)) ?? "",
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.True or JsonValueKind.False => element.GetBoolean().ToString(),
                    JsonValueKind.Array => $"[{element.GetArrayLength()} éléments]",
                    JsonValueKind.Object => "[Objet]",
                    JsonValueKind.Null => "null",
                    _ => element.GetRawText()?.Substring(0, Math.Min(50, element.GetRawText()?.Length ?? 0)) ?? ""
                };
            }
            catch
            {
                return "N/A";
            }
        }

        private static async Task UpdateArticleTagsInDatabaseAsync(Dictionary<string, ArticleTagInfo> detectedTags)
        {
            try
            {
                var connectionString = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"],
                    Database = _configuration["Database:Name"]
                }.ConnectionString;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Récupérer les balises existantes
                    var existingTags = await GetExistingTagsAsync(connection);

                    int newTagsCount = 0;
                    int updatedTagsCount = 0;

                    foreach (var tag in detectedTags.Values)
                    {
                        if (existingTags.ContainsKey(tag.TagName))
                        {
                            // Mettre à jour une balise existante
                            await UpdateExistingTagAsync(connection, tag, existingTags[tag.TagName]);
                            updatedTagsCount++;
                        }
                        else
                        {
                            // Nouvelle balise détectée !
                            await InsertNewTagAsync(connection, tag);
                            newTagsCount++;

                            // Notification de nouvelle balise
                            Console.WriteLine($"🆕 NOUVELLE BALISE DÉTECTÉE: {tag.TagName} (Type: {tag.DataType})");
                            _logger.LogInformation($"Nouvelle balise détectée: {tag.TagName} - Type: {tag.DataType}");
                        }
                    }

                    Console.WriteLine($"✅ Balises mises à jour: {newTagsCount} nouvelles, {updatedTagsCount} existantes");

                    // Log des nouvelles balises dans la table de logs
                    if (newTagsCount > 0)
                    {
                        await LogNewTagsDetectionAsync(connection, newTagsCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la mise à jour des balises");
                Console.WriteLine($"Erreur lors de la mise à jour des balises: {ex.Message}");
            }
        }

        private static async Task<Dictionary<string, ArticleTagInfo>> GetExistingTagsAsync(MySqlConnection connection)
        {
            var existingTags = new Dictionary<string, ArticleTagInfo>();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT tag_name, data_type, first_seen_at, last_seen_at, 
                               occurrence_count, sample_value 
                        FROM article_tags";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var tagInfo = new ArticleTagInfo
                            {
                                TagName = reader.GetString(0),
                                DataType = reader.GetString(1),
                                FirstSeen = reader.GetDateTime(2),
                                LastSeen = reader.GetDateTime(3),
                                OccurrenceCount = reader.GetInt32(4),
                                SampleValue = reader.IsDBNull(5) ? "" : reader.GetString(5)
                            };

                            existingTags[tagInfo.TagName] = tagInfo;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des balises existantes");
            }

            return existingTags;
        }

        private static async Task InsertNewTagAsync(MySqlConnection connection, ArticleTagInfo tag)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    INSERT INTO article_tags (
                        tag_name, data_type, first_seen_at, last_seen_at, 
                        occurrence_count, sample_value
                    ) VALUES (
                        @tag_name, @data_type, @first_seen, @last_seen, 
                        @occurrence_count, @sample_value
                    )";

                command.Parameters.AddWithValue("@tag_name", tag.TagName);
                command.Parameters.AddWithValue("@data_type", tag.DataType);
                command.Parameters.AddWithValue("@first_seen", tag.FirstSeen);
                command.Parameters.AddWithValue("@last_seen", tag.LastSeen);
                command.Parameters.AddWithValue("@occurrence_count", tag.OccurrenceCount);
                command.Parameters.AddWithValue("@sample_value", tag.SampleValue);

                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task UpdateExistingTagAsync(MySqlConnection connection, ArticleTagInfo newTag, ArticleTagInfo existingTag)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    UPDATE article_tags 
                    SET last_seen_at = @last_seen,
                        occurrence_count = occurrence_count + @additional_count,
                        data_type = @data_type,
                        sample_value = @sample_value
                    WHERE tag_name = @tag_name";

                command.Parameters.AddWithValue("@tag_name", newTag.TagName);
                command.Parameters.AddWithValue("@last_seen", newTag.LastSeen);
                command.Parameters.AddWithValue("@additional_count", newTag.OccurrenceCount);
                command.Parameters.AddWithValue("@data_type", newTag.DataType);
                command.Parameters.AddWithValue("@sample_value", newTag.SampleValue);

                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task LogNewTagsDetectionAsync(MySqlConnection connection, int newTagsCount)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    INSERT INTO sync_logs (
                        endpoint, status, message, execution_time_ms
                    ) VALUES (
                        'TAG_DETECTION', 'SUCCESS', @message, 0
                    )";

                command.Parameters.AddWithValue("@message", $"{newTagsCount} nouvelles balises détectées");
                await command.ExecuteNonQueryAsync();
            }
        }

        // ========================================
        // MÉTHODES EXISTANTES CONSERVÉES
        // ========================================

        private static async Task<HashSet<string>> GetExistingArticleIdsAsync(MySqlConnection connection)
        {
            var itemIds = new HashSet<string>();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT item_id FROM articles_raw WHERE item_id IS NOT NULL AND (is_deleted = FALSE OR is_deleted IS NULL)";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var itemId = reader.GetString(0);
                            itemIds.Add(itemId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des ItemIds existants");
            }

            return itemIds;
        }

        private static async Task<string> GetArticleHashAsync(MySqlConnection connection, string itemId)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT content_hash FROM articles_raw WHERE item_id = @item_id";
                    command.Parameters.AddWithValue("@item_id", itemId);

                    var result = await command.ExecuteScalarAsync();
                    return result?.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération du hash pour {itemId}");
                return null;
            }
        }

        private static async Task ForceUpdateArticleAsync(MySqlConnection connection, string itemId, string jsonData, string hash, string endpoint)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    UPDATE articles_raw 
                    SET json_data = @json_data, 
                        content_hash = @hash, 
                        last_updated_at = NOW()
                    WHERE item_id = @item_id";

                command.Parameters.AddWithValue("@json_data", jsonData);
                command.Parameters.AddWithValue("@hash", hash);
                command.Parameters.AddWithValue("@item_id", itemId);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task TouchArticleAsync(MySqlConnection connection, string itemId)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE articles_raw 
                        SET last_updated_at = NOW(),
                            is_deleted = FALSE
                        WHERE item_id = @item_id";

                    command.Parameters.AddWithValue("@item_id", itemId);
                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du touch de l'article {itemId}");
            }
        }

        private static async Task MarkArticleAsDeletedAsync(MySqlConnection connection, string itemId)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE articles_raw 
                        SET is_deleted = TRUE, 
                            deleted_at = NOW(), 
                            last_updated_at = NOW()
                        WHERE item_id = @item_id";

                    command.Parameters.AddWithValue("@item_id", itemId);
                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du marquage de suppression pour l'article {itemId}");
            }
        }

        private static async Task<Dictionary<string, string>> GetExistingArticleHashesAsync(MySqlConnection connection)
        {
            var hashes = new Dictionary<string, string>();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT item_id, content_hash FROM articles_raw WHERE item_id IS NOT NULL AND (is_deleted = FALSE OR is_deleted IS NULL)";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var itemId = reader.GetString(0); // index 0 pour item_id
                            var hash = reader.GetString(1);   // index 1 pour content_hash
                            hashes[itemId] = hash;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des hash existants");
            }

            return hashes;
        }

        private static async Task InsertNewArticleAsync(MySqlConnection connection, string itemId, string jsonData, string hash, string endpoint)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    INSERT INTO articles_raw (json_data, api_endpoint, content_hash, first_seen_at, last_updated_at) 
                    VALUES (@json_data, @endpoint, @hash, NOW(), NOW())";

                command.Parameters.AddWithValue("@json_data", jsonData);
                command.Parameters.AddWithValue("@endpoint", endpoint);
                command.Parameters.AddWithValue("@hash", hash);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task UpdateExistingArticleAsync(MySqlConnection connection, string itemId, string jsonData, string hash, string endpoint)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    UPDATE articles_raw 
                    SET json_data = @json_data, 
                        content_hash = @hash, 
                        last_updated_at = NOW(),
                        update_count = update_count + 1
                    WHERE item_id = @item_id";

                command.Parameters.AddWithValue("@json_data", jsonData);
                command.Parameters.AddWithValue("@hash", hash);
                command.Parameters.AddWithValue("@item_id", itemId);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static string CalculateHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Convert.ToBase64String(hashBytes);
            }
        }

        private static bool CreateDatabaseIfNotExists()
        {
            try
            {
                var connectionStringBuilder = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"]
                };

                using (var connection = new MySqlConnection(connectionStringBuilder.ConnectionString))
                {
                    connection.Open();
                    _logger.LogInformation("✓ Connexion MySQL établie");

                    // Création de la base de données
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_configuration["Database:Name"]}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
                        command.ExecuteNonQuery();
                    }

                    // Utilisation de la base de données
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"USE `{_configuration["Database:Name"]}`";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table pour les articles avec colonnes de suivi
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS articles_raw (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                json_data JSON NOT NULL,
                                content_hash VARCHAR(255) NOT NULL,
                                api_endpoint VARCHAR(255) DEFAULT 'BRINT34ReleasedProducts',
                                item_id VARCHAR(50) GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(json_data, '$.ItemId'))) STORED,
                                first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                update_count INT DEFAULT 0,
                                is_deleted BOOLEAN DEFAULT FALSE,
                                deleted_at TIMESTAMP NULL,
                                INDEX idx_item_id (item_id),
                                INDEX idx_content_hash (content_hash),
                                INDEX idx_api_endpoint (api_endpoint),
                                INDEX idx_last_updated (last_updated_at),
                                INDEX idx_is_deleted (is_deleted),
                                UNIQUE KEY unique_item_id (item_id)
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table des logs
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS sync_logs (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                endpoint VARCHAR(255),
                                status ENUM('SUCCESS', 'ERROR', 'WARNING') DEFAULT 'SUCCESS',
                                total_articles_processed INT DEFAULT 0,
                                new_articles INT DEFAULT 0,
                                updated_articles INT DEFAULT 0,
                                unchanged_articles INT DEFAULT 0,
                                error_count INT DEFAULT 0,
                                message TEXT,
                                execution_time_ms INT,
                                sync_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table d'historique des modifications
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS articles_history (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                item_id VARCHAR(50),
                                old_json_data JSON,
                                new_json_data JSON,
                                change_type ENUM('INSERT', 'UPDATE') DEFAULT 'UPDATE',
                                changed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                INDEX idx_item_id (item_id),
                                INDEX idx_changed_at (changed_at)
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table des balises d'articles
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS article_tags (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                tag_name VARCHAR(191) NOT NULL,
                                data_type VARCHAR(50) NOT NULL,
                                first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                last_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                occurrence_count INT DEFAULT 0,
                                sample_value TEXT,
                                is_active BOOLEAN DEFAULT TRUE,
                                UNIQUE KEY unique_tag_name (tag_name),
                                INDEX idx_data_type (data_type),
                                INDEX idx_last_seen (last_seen_at)
                            )";
                        command.ExecuteNonQuery();
                    }
                }

                _logger.LogInformation("✓ Base de données et tables créées/vérifiées");
                Console.WriteLine("✓ Base de données initialisée");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la création de la base de données");
                Console.WriteLine($"Erreur DB: {ex.Message}");
                return false;
            }
        }

        private static void LogSyncResult(string endpoint, string status, SyncResult result, long executionTimeMs)
        {
            try
            {
                var connectionString = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"],
                    Database = _configuration["Database:Name"]
                }.ConnectionString;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            INSERT INTO sync_logs (
                                endpoint, 
                                status, 
                                total_articles_processed, 
                                new_articles, 
                                updated_articles, 
                                unchanged_articles, 
                                error_count, 
                                message, 
                                execution_time_ms
                            ) VALUES (
                                @endpoint, 
                                @status, 
                                @total_articles_processed, 
                                @new_articles, 
                                @updated_articles, 
                                @unchanged_articles, 
                                @error_count, 
                                @message, 
                                @execution_time
                            )";

                        command.Parameters.AddWithValue("@endpoint", endpoint);
                        command.Parameters.AddWithValue("@status", status);
                        command.Parameters.AddWithValue("@total_articles_processed", result.TotalProcessed);
                        command.Parameters.AddWithValue("@new_articles", result.NewArticles);
                        command.Parameters.AddWithValue("@updated_articles", result.UpdatedArticles);
                        command.Parameters.AddWithValue("@unchanged_articles", result.UnchangedArticles);
                        command.Parameters.AddWithValue("@error_count", result.ErrorCount);
                        command.Parameters.AddWithValue("@message", $"Synchronisation - Nouveaux: {result.NewArticles}, MàJ: {result.UpdatedArticles}, Inchangés: {result.UnchangedArticles}");
                        command.Parameters.AddWithValue("@execution_time", executionTimeMs);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement du log détaillé");
            }
        }

        // Méthode de logging simple pour les erreurs
        private static void LogSyncError(string endpoint, string message, long executionTimeMs)
        {
            try
            {
                var connectionString = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"],
                    Database = _configuration["Database:Name"]
                }.ConnectionString;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                           INSERT INTO sync_logs (
                               endpoint, 
                               status, 
                               total_articles_processed, 
                               new_articles, 
                               updated_articles, 
                               unchanged_articles, 
                               error_count, 
                               message, 
                               execution_time_ms
                           ) VALUES (
                                    @endpoint, 
                                    'ERROR', 
                                    @total_articles_processed, 
                                    @new_articles, 
                                    @updated_articles, 
                                    @unchanged_articles, 
                                    @error_count, 
                                    @message, 
                                    @execution_time
                                )";

                        command.Parameters.AddWithValue("@endpoint", endpoint);
                        command.Parameters.AddWithValue("@total_articles_processed", 0);
                        command.Parameters.AddWithValue("@new_articles", 0);
                        command.Parameters.AddWithValue("@updated_articles", 0);
                        command.Parameters.AddWithValue("@unchanged_articles", 0);
                        command.Parameters.AddWithValue("@error_count", 1);
                        command.Parameters.AddWithValue("@message", message);
                        command.Parameters.AddWithValue("@execution_time", executionTimeMs);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement du log d'erreur");
            }
        }
    }

    // ========================================
    // CLASSES DE SUPPORT
    // ========================================

    public class SyncResult
    {
        public int TotalProcessed { get; set; } = 0;
        public int NewArticles { get; set; } = 0;
        public int UpdatedArticles { get; set; } = 0;
        public int UnchangedArticles { get; set; } = 0;
        public int ErrorCount { get; set; } = 0;
    }

    public class TokenResponse
    {
        public string token_type { get; set; }
        public string scope { get; set; }
        public string expires_in { get; set; }
        public string ext_expires_in { get; set; }
        public string expires_on { get; set; }
        public string not_before { get; set; }
        public string resource { get; set; }
        public string access_token { get; set; }
    }

    public class ArticleTagInfo
    {
        public string TagName { get; set; }
        public string DataType { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int OccurrenceCount { get; set; }
        public string SampleValue { get; set; }
    }
}