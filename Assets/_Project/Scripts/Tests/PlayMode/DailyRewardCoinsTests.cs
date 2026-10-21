#if UNITY_EDITOR
using System;
using System.Collections;
using AssetKits.ParticleImage;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IceP.Tests
{
    public sealed class DailyRewardCoinsTests
    {
        private string previousDirectory;
        private string testDirectory;

        [SetUp]
        public void Setup()
        {
            previousDirectory = Environment.GetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY");
            testDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IceP-Coins-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", testDirectory);
        }

        [TearDown]
        public void Cleanup()
        {
            Time.timeScale = 1;
            Environment.SetEnvironmentVariable("ICEP_TEST_SAVE_DIRECTORY", previousDirectory);
            if (System.IO.Directory.Exists(testDirectory)) System.IO.Directory.Delete(testDirectory, true);
        }

        [UnityTest]
        public IEnumerator ClaimEmitsCoinsAndEveryDayUsesItsOwnCard()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/SC.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var navigation = UnityEngine.Object.FindFirstObjectByType<GameNavigationView>();
            var coins = UnityEngine.Object.FindFirstObjectByType<ParticleImage>();
            Assert.That(coins, Is.Not.Null);
            Assert.That(coins.isPlaying, Is.False, "Opening the menu must not emit coins.");
            Assert.That(coins.attractorTarget, Is.Not.Null);
            Assert.That(coins.raycastTarget, Is.False);
            navigation.DailyBonusButton.onClick.Invoke();
            string balanceBeforeClaim = navigation.BalanceText;
            navigation.DailyBonusView.ClaimButton.onClick.Invoke();
            Assert.That(coins.isPlaying, Is.True, "A confirmed claim must start the effect.");
            Assert.That(navigation.BalanceText, Is.EqualTo(balanceBeforeClaim));
            yield return null;
            Assert.That(coins.particleCount, Is.GreaterThan(0));

            Time.timeScale = 0;
            for (int day = 1; day <= 7; day++)
            {
                navigation.DailyBonusView.PlayRewardCoins(day);
                var source = navigation.DailyBonusView.transform.Find("DailyRewards/RewardDay" + day) as RectTransform;
                Assert.That(source, Is.Not.Null);
                Assert.That(Vector3.Distance(coins.transform.position, source.TransformPoint(source.rect.center)), Is.LessThan(.01f));
            }
            navigation.CloseDailyBonusButton.onClick.Invoke();
            Assert.That(coins.gameObject.activeInHierarchy, Is.True, "Closing the popup must let coins finish.");
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(coins.particleCount, Is.GreaterThan(0), "Coins must animate while the game clock is paused.");
            Assert.That(navigation.BalanceText, Is.EqualTo(balanceBeforeClaim), "Closing the popup must not update the balance early.");
            float deadline = Time.realtimeSinceStartup + 5;
            while (coins.isPlaying && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(navigation.BalanceText, Is.EqualTo(balanceBeforeClaim));
                yield return null;
            }
            yield return null;
            Assert.That(coins.particleCount, Is.Zero);
            Assert.That(coins.isPlaying, Is.False);
            Assert.That(navigation.BalanceText, Is.Not.EqualTo(balanceBeforeClaim));
        }
    }
}
#endif
