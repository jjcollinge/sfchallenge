using Common;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace Logger
{
    public class MongoDBTradeLogger : ITradeLogger
    {
        private IMongoCollection<BsonDocument> collection;

        private MongoDBTradeLogger()
        { }

        public static MongoDBTradeLogger Create(string connectionString, bool enableSSL, string databaseName, string collectionName)
        {
            MongoClientSettings settings = MongoClientSettings.FromUrl(
              new MongoUrl(connectionString)
            );

            if (enableSSL)
            {
                settings.SslSettings =
                  new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
            }

            var client = new MongoClient(settings);
            var db = client.GetDatabase(databaseName);  // auto creates if not exist
            var collection = db.GetCollection<BsonDocument>(collectionName);    // auto creates if not exist

            var logger = new MongoDBTradeLogger()
            {
                collection = collection
            };
            return logger;
        }

        public async Task InsertAsync(Trade trade, CancellationToken cancellationToken)
        {
            try
            {
                var doc = trade.ToBsonDocument();
                var res = await collection.ReplaceOneAsync(
                    filter: new BsonDocument("_id", trade.Id),
                    options: new UpdateOptions { IsUpsert = true },
                    replacement: doc,
                    cancellationToken: cancellationToken);
            }
            catch (MongoConnectionException ex)
            {
                throw new LoggerDisconnectedException($"Mongo connection issue {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InsertFailedException($"Error writing trade '{trade.Id}' to MongoDB, error: '{ex.Message}' {ex.ToString()}");
            }
        }

        public async Task ClearAsync(CancellationToken cancellationToken)
        {
            await collection.Database.DropCollectionAsync(collection.CollectionNamespace.CollectionName, cancellationToken);
        }

        public async Task<long> CountAsync(CancellationToken cancellationToken)
        {
            return await collection.CountAsync(new BsonDocument());
        }
    }
}
