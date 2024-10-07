using BepInEx;
using R2API;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WebSocketSharp;

namespace ExamplePlugin
{
    [BepInDependency(ItemAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ExamplePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "AuthorName";
        public const string PluginName = "ExamplePlugin";
        public const string PluginVersion = "1.0.0";

        private WebSocket ws;
        private Queue<Action> spawnQueue = new Queue<Action>();

        private int spawnHeight = 4;

        private void Awake()
        {
            Log.Init(Logger);
            StartCoroutine(ProcessSpawnQueue());
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D))
            {
                spawnHeight = 4;
            }

            if (Input.GetKeyDown(KeyCode.F2))
            {
                ws = new WebSocket("ws://localhost:6789");
                ws.OnMessage += Ws_OnMessage;

                ws.Connect();
            }
        }

        private void Ws_OnMessage(object sender, MessageEventArgs e)
        {
            try
            {
                Log.Info(e.Data);
                var jsonArray = SimpleJSON.JSON.Parse(e.Data).AsArray;

                Log.Info(jsonArray);
                for (int i = 0; i < jsonArray.Count; i++)
                {
                    string eventType = jsonArray[i]["event"];
                    string entity = jsonArray[i]["monster"] ?? jsonArray[i]["item"];

                    switch (eventType)
                    {
                        case "spawn_boss":
                        case "spawn_enemy":
                            EnqueueSpawn(() => SpawnEnemyAtPlayer($"{entity}Master"));
                            spawnHeight += 4;
                            break;
                        case "spawn_item":
                            ItemTier itemTier = DetermineItemTier(jsonArray[i]["item"]);
                            EnqueueSpawn(() => GiveRandomItem(itemTier));
                            break;
                        default:
                            Log.Warning($"Unhandled WebSocket event type: {eventType}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to handle WebSocket message: {ex.Message}");
            }
        }

        private void EnqueueSpawn(Action spawnAction)
        {
            spawnQueue.Enqueue(spawnAction);
        }

        private IEnumerator ProcessSpawnQueue()
        {
            while (true)
            {
                if (spawnQueue.Count > 0)
                {
                    var spawnAction = spawnQueue.Dequeue();
                    spawnAction.Invoke();
                }
                yield return new WaitForSeconds(0.1f); // Spawn at intervals of 0.1 seconds
            }
        }

        private ItemTier DetermineItemTier(string itemName)
        {
            ItemTier itemTier = ItemTier.Tier1;
            Log.Info(itemName);
            switch (itemName)
            {
                case "Tier2":
                    itemTier = ItemTier.Tier2;
                    break;
                case "Tier3":
                    itemTier = ItemTier.Tier3;
                    break;
                case "Tier4":
                    itemTier = ItemTier.Boss;
                    break;
            }

            return itemTier;
        }

        private void SpawnEnemyAtPlayer(string enemyName)
        {
            var playerPosition = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform.position;
            var position = playerPosition + new Vector3(0, spawnHeight, 0);

            if (enemyName != null)
            {
                var enemyMasterPrefab = MasterCatalog.FindMasterPrefab(enemyName);
                if (enemyMasterPrefab != null)
                {
                    var summoner = new MasterSummon
                    {
                        masterPrefab = enemyMasterPrefab,
                        position = position,
                        rotation = Quaternion.identity,
                        summonerBodyObject = null,
                        ignoreTeamMemberLimit = true,
                        teamIndexOverride = TeamIndex.Monster
                    }.Perform();

                    // Apply stats to the enemy
                    float healthBoostCoefficient = 1f;
                    float damageBoostCoefficient = 1f;
                    healthBoostCoefficient += Run.instance.difficultyCoefficient;
                    damageBoostCoefficient += Run.instance.difficultyCoefficient;
                    summoner.inventory.GiveItem(RoR2Content.Items.BoostHp, Mathf.RoundToInt(((healthBoostCoefficient - 1f) * 10) / 2));
                    summoner.inventory.GiveItem(RoR2Content.Items.BoostDamage, Mathf.RoundToInt(((damageBoostCoefficient - 1f) * 10) / 2));
                    Log.Info($"Spawned enemy {enemyName} at position {position}");
                }
                else
                {
                    Log.Warning($"Enemy prefab not found for {enemyName}");
                }
            }
            else
            {
                Log.Warning("Invalid enemy name");
            }
        }

        private void GiveRandomItem(ItemTier itemTier)
        {
            var characterMaster = PlayerCharacterMasterController.instances[0].master;
            var transform = characterMaster.GetBodyObject().transform;
            ItemDef randomItem = GetRandomItem(itemTier);

            if (randomItem != null)
            {
                Log.Info($"Spawning a random {itemTier} item at coordinates {transform.position} with name: {randomItem.name}");
                characterMaster.inventory.GiveItem(randomItem);
            }
            else
            {
                Log.Warning($"No items found for tier: {itemTier}");
            }
        }

        private ItemDef GetRandomItem(ItemTier itemTier)
        {
            var items = ItemCatalog.itemDefs
                .Where(item => item.tier == itemTier)
                .ToArray();

            if (items.Length == 0)
                return null;

            var randomItem = items[UnityEngine.Random.Range(0, items.Length)];
            return randomItem;
        }
    }
}
