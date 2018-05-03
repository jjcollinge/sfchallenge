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
    public class MongoDBTransferLogger: ITransferLogger
    {
        private IMongoDatabase database;
        private string collectionName;

        private MongoDBTransferLogger()
        {}

        public static MongoDBTransferLogger Create(string connectionString, string databaseName, string collectionName)
        {
            MongoClientSettings settings = MongoClientSettings.FromUrl(
              new MongoUrl(connectionString)
            );
            settings.SslSettings =
              new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
            var mongoClient = new MongoClient(settings);

            var db = new MongoDBTransferLogger();
            db.database = mongoClient.GetDatabase(databaseName);
            db.collectionName = collectionName;
            return db;
        }

        public async Task InsertAsync(Transfer transfer)
        {
            if (database.GetCollection<BsonDocument>(this.collectionName) == null)
            {
                await database.CreateCollectionAsync(this.collectionName);
            }

            var collection = database.GetCollection<BsonDocument>(this.collectionName);
            try
            {
                var doc = transfer.ToBsonDocument();
                await collection.InsertOneAsync(doc);
            }
            catch (MongoDB.Driver.MongoWriteException ex)
            {
                ServiceEventSource.Current.Message($"Error writing transfer '{transfer.Id}' to MongoDB, error: '{ex.Message}'");
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
