using System.Collections.Generic;
using NUnit.Framework;
using OpenDesk.Core.Implementations;
using R3;

namespace OpenDesk.Core.Tests
{
    public class CostMonitorServiceTests
    {
        private CostMonitorService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new CostMonitorService();
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        [Test]
        public void 초기값_모두0()
        {
            Assert.AreEqual(0m,  _service.CurrentSessionCost.CurrentValue);
            Assert.AreEqual(0L,  _service.TotalTokensUsed.CurrentValue);
            Assert.AreEqual(0L,  _service.TokensSavedByCache.CurrentValue);
        }

        [Test]
        public void ReportTokenUsage_비용누적()
        {
            _service.ReportTokenUsage(100, 50, 0.05m);
            _service.ReportTokenUsage(200, 100, 0.10m);

            Assert.AreEqual(0.15m, _service.CurrentSessionCost.CurrentValue);
            Assert.AreEqual(450L,  _service.TotalTokensUsed.CurrentValue);
        }

        [Test]
        public void ReportTokenUsage_캐시토큰_누적()
        {
            _service.ReportTokenUsage(100, 50, 0.05m, cachedTokens: 30);
            _service.ReportTokenUsage(200, 100, 0.10m, cachedTokens: 70);

            Assert.AreEqual(100L, _service.TokensSavedByCache.CurrentValue);
        }

        [Test]
        public void ResetSession_초기화()
        {
            _service.ReportTokenUsage(100, 50, 0.05m);
            _service.ResetSession();

            Assert.AreEqual(0m, _service.CurrentSessionCost.CurrentValue);
            Assert.AreEqual(0L, _service.TotalTokensUsed.CurrentValue);
        }

        [Test]
        public void SetCostAlertThreshold_초과시_경고발행()
        {
            var alerts = new List<decimal>();
            _service.OnCostAlert.Subscribe(c => alerts.Add(c));

            _service.SetCostAlertThreshold(1m);
            _service.ReportTokenUsage(1000, 500, 0.50m); // $0.50 → 미초과
            Assert.AreEqual(0, alerts.Count);

            _service.ReportTokenUsage(1000, 500, 0.60m); // $1.10 → 초과
            Assert.AreEqual(1, alerts.Count);
            Assert.AreEqual(1.10m, alerts[0]);
        }
    }
}
