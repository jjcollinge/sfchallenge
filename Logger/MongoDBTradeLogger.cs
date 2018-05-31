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
        private string connectionString;
        private bool enableSSL;
        private string databaseName;
        private string collectionName;

        private MongoDBTradeLogger()
        { }

        public static MongoDBTradeLogger Create(string connectionString, bool enableSSL, string databaseName, string collectionName)
        {
            IMongoCollection<BsonDocument> collection = GetOrCreateCollection(connectionString, enableSSL, databaseName, collectionName);

            var logger = new MongoDBTradeLogger()
            {
                collection = collection,
                connectionString = connectionString,
                enableSSL = enableSSL,
                databaseName = databaseName,
                collectionName = collectionName
            };
            return logger;
        }

        private static IMongoCollection<BsonDocument> GetOrCreateCollection(string connectionString, bool enableSSL, string databaseName, string collectionName)
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
            return collection;
        }

        public async Task InsertAsync(Trade trade, CancellationToken cancellationToken)
        {
            try
            {
                if (collection == null)
                {
                    collection = GetOrCreateCollection(connectionString, enableSSL, databaseName, collectionName);
                }
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
                throw new InsertFailedException($"Error writing trade '{trade.Id}' to MongoDB, error: '{ex.Message}'");
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
