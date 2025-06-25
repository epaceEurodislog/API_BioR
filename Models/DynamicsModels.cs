// Fichier: Models/DynamicsModels.cs
// Classes de données pour l'API Dynamics et la synchronisation

using System;

namespace DynamicsApiToDatabase.Models
{
    /// <summary>
    /// Réponse d'authentification de l'API Dynamics
    /// </summary>
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

    /// <summary>
    /// Configuration d'un endpoint de commandes
    /// </summary>
    public class OrderEndpoint
    {
        public string Name { get; set; }
        public string Endpoint { get; set; }
        public string TableName { get; set; }
        public string PrimaryKeyField { get; set; }
        public string LineNumberField { get; set; }
        public string DisplayName { get; set; }
    }

    /// <summary>
    /// Résultat de synchronisation des articles
    /// </summary>
    public class SyncResult
    {
        public int TotalProcessed { get; set; } = 0;
        public int NewArticles { get; set; } = 0;
        public int UpdatedArticles { get; set; } = 0;
        public int UnchangedArticles { get; set; } = 0;
        public int ErrorCount { get; set; } = 0;
    }

    /// <summary>
    /// Résultat de synchronisation des commandes
    /// </summary>
    public class OrderSyncResult
    {
        public int TotalProcessed { get; set; } = 0;
        public int NewOrderLines { get; set; } = 0;
        public int UpdatedOrderLines { get; set; } = 0;
        public int UnchangedOrderLines { get; set; } = 0;
        public int ErrorCount { get; set; } = 0;
        public string OrderType { get; set; } = "";
    }

    /// <summary>
    /// Information sur les balises d'articles détectées
    /// </summary>
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