using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SocialValley
{
    public class ModConfig
    {
        // Configuración existente
        public string Language { get; set; } = "Auto";
        public string OpenChatKey { get; set; } = "MouseLeft";
        public bool EnableDebugLogging { get; set; } = false;
        
        // ===== NUEVA SECCIÓN: Configuración de IA =====
        
        // Proveedor seleccionado
        public AIProvider SelectedProvider { get; set; } = AIProvider.Player2;
        
        // API Keys por proveedor
        public string Player2ApiKey { get; set; } = "";
        public string OllamaApiKey { get; set; } = "";
        public string OpenRouterApiKey { get; set; } = "";
        public string GoogleApiKey { get; set; } = "";
        public string OpenAIApiKey { get; set; } = "";
        
        // Modelos seleccionados por proveedor
        public string Player2SelectedModel { get; set; } = "";
        public string OllamaSelectedModel { get; set; } = "";
        public string OpenRouterSelectedModel { get; set; } = "";
        public string GoogleSelectedModel { get; set; } = "gemini-1.5-flash";
        public string OpenAISelectedModel { get; set; } = "gpt-4o-mini";
        
        // ===== GLOBAL INSTRUCTIONS =====
        
        /// <summary>
        /// Nombre con el que los NPCs se referirán al jugador.
        /// Si está vacío, se usa Game1.player.Name automáticamente.
        /// </summary>
        public string PlayerNickname { get; set; } = "";
        
        /// <summary>
        /// Instrucciones globales que se añaden al system prompt de TODOS los NPCs.
        /// Útil para definir reglas de formato, idioma de respuesta, etc.
        /// </summary>
        public string GlobalInstructions { get; set; } = "";
    }
    
    public class NPCPersonalityConfig
    {
        public bool UseCustom { get; set; } = false;
        public string CustomPersonality { get; set; } = "";
    }
    
    public class ConfigManager
    {
        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private ModConfig config;
        private readonly string personalitiesFolder;
        private Dictionary<string, NPCPersonalityConfig> npcConfigs = new Dictionary<string, NPCPersonalityConfig>();
        
        public ModConfig Config => config;
        
        public KeybindList GetChatKeyBind()
        {
            if (Enum.TryParse<SButton>(config.OpenChatKey, out var button))
            {
                return new KeybindList(button);
            }
            return new KeybindList(SButton.MouseLeft);
        }
        
        public SButton GetChatButton()
        {
            if (Enum.TryParse<SButton>(config.OpenChatKey, out var button))
            {
                return button;
            }
            return SButton.MouseLeft;
        }
        
        public ConfigManager(IModHelper helper, IMonitor monitor)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.personalitiesFolder = Path.Combine(helper.DirectoryPath, "personalities");
            
            LoadConfig();
            EnsurePersonalitiesFolder();
            LoadAllNPCConfigs();
        }
        
        private void LoadConfig()
        {
            config = helper.ReadConfig<ModConfig>();
            monitor.Log($"Config loaded - Language: {config.Language}, Key: {config.OpenChatKey}, Provider: {config.SelectedProvider}", LogLevel.Info);
        }
        
        public void SaveConfig()
        {
            helper.WriteConfig(config);
            monitor.Log("Configuration saved to config.json", LogLevel.Info);
        }
        
        // ===== MÉTODOS EXISTENTES PARA PERSONALIDADES =====
        
        private void EnsurePersonalitiesFolder()
        {
            if (!Directory.Exists(personalitiesFolder))
            {
                Directory.CreateDirectory(personalitiesFolder);
                monitor.Log($"Created personalities folder at: {personalitiesFolder}", LogLevel.Info);
            }
        }
        
        private void LoadAllNPCConfigs()
        {
            npcConfigs.Clear();
            
            if (!Directory.Exists(personalitiesFolder))
                return;
                
            var files = Directory.GetFiles(personalitiesFolder, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var npcName = Path.GetFileNameWithoutExtension(file);
                    var json = File.ReadAllText(file);
                    var config = JsonConvert.DeserializeObject<NPCPersonalityConfig>(json);
                    if (config != null)
                    {
                        npcConfigs[npcName] = config;
                        monitor.Log($"Loaded personality config for {npcName}", LogLevel.Debug);
                    }
                }
                catch (Exception ex)
                {
                    monitor.Log($"Error loading personality config from {file}: {ex.Message}", LogLevel.Error);
                }
            }
        }
        
        public NPCPersonalityConfig GetNPCConfig(string npcName)
        {
            if (npcConfigs.ContainsKey(npcName))
            {
                return npcConfigs[npcName];
            }
            
            return new NPCPersonalityConfig
            {
                UseCustom = false,
                CustomPersonality = ""
            };
        }
        
        public void SaveNPCConfig(string npcName, bool useCustom, string customPersonality)
        {
            try
            {
                var config = new NPCPersonalityConfig
                {
                    UseCustom = useCustom,
                    CustomPersonality = customPersonality
                };
                
                npcConfigs[npcName] = config;
                
                var safeName = GetSafeFileName(npcName);
                var filePath = Path.Combine(personalitiesFolder, $"{safeName}.json");
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(filePath, json);
                
                monitor.Log($"Saved personality config for {npcName}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Error saving personality config for {npcName}: {ex.Message}", LogLevel.Error);
            }
        }
        
        public void DeleteNPCConfig(string npcName)
        {
            try
            {
                var safeName = GetSafeFileName(npcName);
                var filePath = Path.Combine(personalitiesFolder, $"{safeName}.json");
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    npcConfigs.Remove(npcName);
                    monitor.Log($"Deleted personality config for {npcName}", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"Error deleting personality config for {npcName}: {ex.Message}", LogLevel.Error);
            }
        }
        
        private string GetSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safeName = name;
            
            foreach (char c in invalid)
            {
                safeName = safeName.Replace(c, '_');
            }
            
            return safeName;
        }
        
        public void UpdateLanguage(string language)
        {
            config.Language = language;
            SaveConfig();
        }
        
        public void UpdateChatKey(SButton key)
        {
            config.OpenChatKey = key.ToString();
            SaveConfig();
        }
        
        public void ResetToDefaults()
        {
            config.Language = "Auto";
            config.OpenChatKey = "MouseLeft";
            SaveConfig();
        }
        
        // ===== NUEVOS MÉTODOS PARA PROVEEDORES DE IA =====
        
        public AIProvider GetSelectedProvider()
        {
            return config.SelectedProvider;
        }
        
        public void UpdateProvider(AIProvider provider)
        {
            config.SelectedProvider = provider;
            SaveConfig();
            monitor.Log($"AI Provider updated to: {provider}", LogLevel.Info);
        }
        
        public string GetApiKeyForProvider(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Player2 => config.Player2ApiKey,
                AIProvider.OpenRouter => config.OpenRouterApiKey,
                AIProvider.Google => config.GoogleApiKey,
                AIProvider.OpenAI => config.OpenAIApiKey,
                _ => ""
            };
        }
        
        public void SetApiKeyForProvider(AIProvider provider, string apiKey)
        {
            switch (provider)
            {
                case AIProvider.Player2:
                    config.Player2ApiKey = apiKey;
                    break;
                case AIProvider.OpenRouter:
                    config.OpenRouterApiKey = apiKey;
                    break;
                case AIProvider.Ollama:
                    config.OllamaApiKey = apiKey;
                    break;
                case AIProvider.Google:
                    config.GoogleApiKey = apiKey;
                    break;
                case AIProvider.OpenAI:
                    config.OpenAIApiKey = apiKey;
                    break;
            }
            SaveConfig();
            monitor.Log($"API Key updated for {provider}", LogLevel.Info);
        }
        
        public string GetSelectedModelForProvider(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Player2 => config.Player2SelectedModel,
                AIProvider.Ollama => config.OllamaSelectedModel,
                AIProvider.OpenRouter => config.OpenRouterSelectedModel,
                AIProvider.Google => config.GoogleSelectedModel,
                AIProvider.OpenAI => config.OpenAISelectedModel,
                _ => ""
            };
        }
        
        public void SetSelectedModelForProvider(AIProvider provider, string model)
        {
            switch (provider)
            {
                case AIProvider.Player2:
                    config.Player2SelectedModel = model;
                    break;
                case AIProvider.Ollama:
                    config.OllamaSelectedModel = model;
                    break;
                case AIProvider.OpenRouter:
                    config.OpenRouterSelectedModel = model;
                    break;
                case AIProvider.Google:
                    config.GoogleSelectedModel = model;
                    break;
                case AIProvider.OpenAI:
                    config.OpenAISelectedModel = model;
                    break;
            }
            SaveConfig();
            monitor.Log($"Model updated for {provider}: {model}", LogLevel.Info);
        }
        
        public bool IsProviderConfigured(AIProvider provider)
        {
            if (provider == AIProvider.None)
                return false;
                
            string apiKey = GetApiKeyForProvider(provider);
            string model = GetSelectedModelForProvider(provider);
            
            // Player2 solo requiere API key, no modelo
            if (provider == AIProvider.Player2)
            {
                return !string.IsNullOrEmpty(apiKey);
            }

            // Ollama requires no API key, but does require model selection
            if (provider == AIProvider.Ollama)
            {
                return !string.IsNullOrEmpty(model);
            }
            
            // Otros proveedores requieren ambos
            bool hasApiKey = !string.IsNullOrEmpty(apiKey);
            bool hasModel = !string.IsNullOrEmpty(model);
            
            return hasApiKey && hasModel;
        }
        
        public string GetCurrentApiKey()
        {
            return GetApiKeyForProvider(config.SelectedProvider);
        }
        
        public string GetCurrentModel()
        {
            return GetSelectedModelForProvider(config.SelectedProvider);
        }
        
        // ===== GLOBAL INSTRUCTIONS =====
        
        /// <summary>
        /// Retorna el nombre con el que los NPCs se referirán al jugador.
        /// Si el usuario no definió uno, usa el nombre real del juego.
        /// </summary>
        public string GetEffectivePlayerName()
        {
            if (!string.IsNullOrWhiteSpace(config.PlayerNickname))
                return config.PlayerNickname.Trim();
            
            try { return StardewValley.Game1.player?.Name ?? "the farmer"; }
            catch { return "the farmer"; }
        }
        
        public string GetPlayerNickname() => config.PlayerNickname;
        
        public void SetPlayerNickname(string nickname)
        {
            config.PlayerNickname = nickname?.Trim() ?? "";
            SaveConfig();
        }
        
        public string GetGlobalInstructions() => config.GlobalInstructions;
        
        public void SetGlobalInstructions(string instructions)
        {
            config.GlobalInstructions = instructions ?? "";
            SaveConfig();
        }
    }
}