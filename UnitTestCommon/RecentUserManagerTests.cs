using NUnit.Framework;
using Seeker;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnitTestCommon
{
    [TestFixture]
    public sealed class RecentUserManagerTests
    {
        [Test]
        public void AddUserToTop_PersistSnapshotIsReReadSoALaterMutationIsNotLost()
        {
            var persisted = new List<IReadOnlyList<string>>();
            RecentUserManager? manager = null;
            bool reentered = false;
            manager = new RecentUserManager(snapshot =>
            {
                persisted.Add(snapshot);
                if (!reentered)
                {
                    reentered = true;
                    manager!.AddUserToTop("during-persist", andSave: true);
                }
            });

            manager.AddUserToTop("first", andSave: true);

            Assert.Multiple(() =>
            {
                Assert.That(persisted[0], Is.EqualTo(new[] { "first" }));
                Assert.That(persisted.Last(), Is.EqualTo(new[] { "during-persist", "first" }));
                Assert.That(persisted.Last(), Is.EqualTo(manager.GetRecentUserList()));
            });
        }

        [Test]
        public void AddUserToTop_ConcurrentSaves_DurablyLastSnapshotReflectsNewestState()
        {
            // Persist invocations are serialized on a dedicated lock, so the list append is safe here.
            var persisted = new List<IReadOnlyList<string>>();
            var manager = new RecentUserManager(snapshot => persisted.Add(snapshot));

            Parallel.For(0, 64, i => manager.AddUserToTop("user" + i, andSave: true));

            Assert.Multiple(() =>
            {
                Assert.That(persisted, Is.Not.Empty);
                Assert.That(manager.GetRecentUserList(), Has.Count.EqualTo(64));
                // The core ordering guarantee: whatever callback ran last carried the newest state, so
                // two racing saves can never durably write an older snapshot after a newer one.
                Assert.That(persisted.Last(), Is.EqualTo(manager.GetRecentUserList()));
            });
        }
    }
}
