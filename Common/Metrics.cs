using Microsoft.ApplicationInsights;
using System.Collections.Generic;

namespace Common
{
    /// <summary>
    /// Gathering Metrics about the performance of the services.
    /// These are used to compare each team agains each other.
    /// </summary>
    public class Metrics
    {
        private readonly TelemetryClient _tc;
        private readonly string _teamName;

        public Metrics(string instrumentationKey, string teamName)
        {
            _tc = new TelemetryClient
            {
                InstrumentationKey = instrumentationKey
            };
            _tc.Context.User.Id = teamName;
            _teamName = teamName;
        }

        #region Fulfillment Metrics

        public enum TradedStatus
        {
            Completed = 0,
            Failed = 2,
        }
        public void Traded(Trade trade, TradedStatus status)
        {
            var properties = new Dictionary<string, string>() {
                { "AskUserId", trade.Ask.UserId },
                { "BidUserId", trade.Bid.UserId },
                { "TradeId", trade.Id },
                { "Status", status.ToString("D") },
                { "TeamName", _teamName }
            };
            var metrics = new Dictionary<string, double>() {
                { "Spread", trade.Ask.Price - trade.Bid.Price },
                { "Volume", trade.Bid.Amount },
            };
            _tc.TrackEvent("Trade completed", properties, metrics);
        }
        #endregion

        #region OrderBook Metrics
        public void BidCreated(Order order)
        {
            var properties = new Dictionary<string, string>() {
                { "userId", order.UserId },
                { "TeamName", _teamName }
            };
            var metrics = new Dictionary<string, double>() {
                { "Amount", order.Amount },
                { "Price", order.Price },
            };
            _tc.TrackEvent("Bid added", properties, metrics);
        }

        public void AskCreated(Order order)
        {
            var properties = new Dictionary<string, string>() {
                { "userId", order.UserId },
                { "TeamName", _teamName }
            };
            var metrics = new Dictionary<string, double>() {
                { "Amount", order.Amount },
                { "Price", order.Price },
            };
            _tc.TrackEvent("Ask added", properties, metrics);
        }

        public void OrderMatched(Order bid, Order ask)
        {
            var properties = new Dictionary<string, string>() {
                { "BidUserId", bid.UserId },
                { "AskUserId", ask.UserId },
                { "TeamName", _teamName }
            };
            var metrics = new Dictionary<string, double>() {
                { "BidAmount", bid.Amount },
                { "BidPrice", bid.Price },
                { "AskAmount", ask.Amount },
                { "AskPrice", ask.Price },
            };
            _tc.TrackEvent("New order match", properties, metrics);
        }
        #endregion

        #region UserStore Metrics
        public void UserCreated(User user)
        {
            var properties = new Dictionary<string, string>() {
                { "userId", user.Id },
                { "TeamName", _teamName }
            };
            _tc.TrackEvent("User Created", properties);
        }

        public void UserUpdated(User user)
        {
            var properties = new Dictionary<string, string>() {
                { "userId", user.Id },
                { "TeamName", _teamName }
            };
            _tc.TrackEvent("User updated", properties);
        }

        #endregion
        ~Metrics()
        {
            _tc.Flush();
        }
    }
}