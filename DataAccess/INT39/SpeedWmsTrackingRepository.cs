// Fichier: DataAccess/INT39/SpeedWmsTrackingRepository.cs
// Repository pour marquer les tracking numbers comme traités dans SpeedWMS
// Table source: OPE_DAT

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.DataAccess.INT39
{
    public class SpeedWmsTrackingRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<SpeedWmsTrackingRepository> _logger;

        public SpeedWmsTrackingRepository(
            IConfiguration configuration, 
            ILogger<SpeedWmsTrackingRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("SpeedWmsConnection")
                ?? throw new ArgumentNullException("SpeedWmsConnection manquante");
            _logger = logger;
        }

        /// <summary>
        /// Marque les tracking numbers comme traités dans SpeedWMS
        /// NOTE: Désactivé temporairement - colonne OPE_TOP39 n'existe pas
        /// La traçabilité se fait uniquement via JSON_OUT
        /// </summary>
        public async Task MarkTrackingAsProcessedAsync(
            List<string> opeKeyuList, 
            string importId,
            bool success)
        {
            // TODO: Créer la colonne OPE_TOP39 dans OPE_DAT si besoin de marquage dans SpeedWMS
            // Pour l'instant, la traçabilité se fait uniquement via JSON_OUT
            _logger.LogDebug($"⏭️ Marquage SpeedWMS désactivé pour {opeKeyuList?.Count ?? 0} tracking numbers (colonne OPE_TOP39 inexistante)");
            await Task.CompletedTask;
        }
    }
}
