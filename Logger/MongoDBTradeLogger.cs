using Common;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace Logger
{
    public class MongoDBTradeLogger: ITradeLogger
    {
        private IMongoDatabase database;
        private string collectionName;

        private MongoDBTradeLogger()
        {}

        public static MongoDBTradeLogger Create(string connectionString, string databaseName, string collectionName)
        {
            MongoClientSettings settings = MongoClientSettings.FromUrl(
              new MongoUrl(connectionString)
            );
            settings.SslSettings =
              new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
            var mongoClient = new MongoClient(settings);

            var db = new MongoDBTradeLogger();
            db.database = mongoClient.GetDatabase(databaseName);
            db.collectionName = collectionName;
            return db;
        }

        public async Task InsertAsync(Trade trade)
        {
            if (database.GetCollection<BsonDocument>(this.collectionName) == null)
            {
                await database.CreateCollectionAsync(this.collectionName);
            }

            var collection = database.GetCollection<BsonDocument>(this.collectionName);
            try
            {
                var doc = trade.ToBsonDocument();
                await collection.InsertOneAsync(doc);
            }
            catch (MongoDB.Driver.MongoWriteException ex)
            {
                ServiceEventSource.Current.Message($"Error writing trade '{trade.Id}' to MongoDB, error: '{ex.Message}'");
            }
        }

        public async Task ClearAsync()
        {
            if (database.GetCollection<BsonDocument>(this.collectionName) != null)
            {
                await database.DropCollectionAsync(this.collectionName);
            }
        }
    }
}
