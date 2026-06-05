using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SocialValley
{
    public class SettingsModal : IClickableMenu
    {
        private readonly ChatUI parentChatUI;
        private readonly IMonitor monitor;
        
        // ===== CONSTANTES PLAYER2 =====
        private const string GameClientId = "01988480-9aca-751f-8351-cc3c505058ad";
        private const string Player2WebApiUrl = "https://api.player2.game/v1";

        // ===== TABS =====
        private enum SettingsTab { General, AIProvider }
        private SettingsTab currentTab = SettingsTab.General;
        private List<ClickableComponent> tabButtons;
        
        // ===== UI Components - General Tab =====
        private ClickableTextureComponent? saveButton;
        private ClickableTextureComponent? cancelButton;
        private ClickableTextureComponent? resetKeyButton;
        private List<ClickableComponent> languageOptions;

        //  Player Name input (simple, una línea)
        private ClickableTextureComponent? playerNameInputBox;
        //  Botón que abre GlobalInstructionsModal
        private ClickableTextureComponent? globalInstructionsButton;
        
        // ===== UI Components - AI Provider Tab =====
        private List<ClickableComponent> providerOptions;
        private ClickableTextureComponent? apiKeyInputBox;
        private ClickableTextureComponent? testConnectionButton;
        private ClickableTextureComponent? fetchModelsButton;
        private ClickableTextureComponent? modelSearchBox;
        private ClickableTextureComponent? modelScrollUpButton;
        private ClickableTextureComponent? modelScrollDownButton;
        private List<ClickableComponent> modelOptions;
        
        // ===== Player2 specific buttons =====
        private ClickableTextureComponent? detectPlayer2Button;
        private ClickableTextureComponent? deviceAuthButton;
        private ClickableTextureComponent? cancelDeviceAuthButton;
        
        // ===== Current selections - General =====
        private string selectedLanguage = "English";
        private SButton selectedKey = SButton.MouseLeft;
        private bool isWaitingForKeyPress = false;

        //  Player Name
        internal string playerNickname = "";
        private bool isEditingPlayerName = false;
        
        // ===== Current selections - AI Provider =====
        private AIProvider selectedProvider = AIProvider.Player2;
        private string currentApiKey = "";
        private string currentModel = "";
        private List<AIModelInfo> availableModels = new List<AIModelInfo>();
        private string modelSearchText = "";
        private int modelScrollOffset = 0;
        private const int MAX_VISIBLE_MODELS = 5;
        private bool isFetchingModels = false;
        private bool isTestingConnection = false;
        private bool isDetectingPlayer2 = false;
        private string connectionStatus = "";
        
        // ===== Device Code OAuth state =====
        private bool isDoingDeviceAuth = false;
        private string deviceUserCode = "";
        private string devicePollCode = "";
        private int devicePollInterval = 5;
        private CancellationTokenSource? deviceAuthCts;
        
        // ===== Visual properties =====
        private readonly int modalWidth = 900;
        private readonly int modalHeight = 720;
        private readonly Color backgroundColor = new Color(40, 40, 40, 240);
        private bool isEditingApiKey = false;
        private bool isEditingModelSearch = false;
        
        public SettingsModal(ChatUI parentChatUI, IMonitor monitor)
            : base((Game1.uiViewport.Width - 900) / 2, (Game1.uiViewport.Height - 720) / 2, 900, 720)
        {
            this.parentChatUI = parentChatUI;
            this.monitor = monitor;
            this.languageOptions = new List<ClickableComponent>();
            this.providerOptions = new List<ClickableComponent>();
            this.modelOptions = new List<ClickableComponent>();
            this.tabButtons = new List<ClickableComponent>();
            
            InitializeComponents();
            LoadCurrentSettings();
            
            if (selectedProvider == AIProvider.Player2)
                UpdatePlayer2StatusDisplay();
        }
        
        private void InitializeComponents()
        {
            int tabWidth = 150;
            int tabY = yPositionOnScreen + 70;
            
            tabButtons.Add(new ClickableComponent(
                new Rectangle(xPositionOnScreen + 40, tabY, tabWidth, 40), "General"));
            tabButtons.Add(new ClickableComponent(
                new Rectangle(xPositionOnScreen + 40 + tabWidth + 15, tabY, tabWidth, 40), "AI Provider"));
            
            InitializeGeneralTab();
            InitializeAIProviderTab();
            
            saveButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + modalWidth - 200, yPositionOnScreen + modalHeight - 60, 90, 40),
                null, Rectangle.Empty, 1f);
            cancelButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + modalWidth - 100, yPositionOnScreen + modalHeight - 60, 90, 40),
                null, Rectangle.Empty, 1f);
        }
        
        private void InitializeGeneralTab()
        {
            var languages = new[] { "English", "Spanish", "French", "German", "Italian", "Portuguese", "Japanese", "Chinese" };
            int yOffset = yPositionOnScreen + 165;
            
            foreach (var lang in languages)
            {
                languageOptions.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + 60, yOffset, 220, 35), lang));
                yOffset += 42;
            }
            
            resetKeyButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + 500, yPositionOnScreen + 215, 140, 40),
                null, Rectangle.Empty, 1f);

            // Bounds placeholder — se reposicionan en Draw cada frame
            playerNameInputBox = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + 500, yPositionOnScreen + 400, 280, 40),
                null, Rectangle.Empty, 1f);

            globalInstructionsButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + 500, yPositionOnScreen + 548, 280, 42),
                null, Rectangle.Empty, 1f);
        }
        
        private void InitializeAIProviderTab()
        {
            int leftColumnX = xPositionOnScreen + 50;
            int rightColumnX = xPositionOnScreen + 380;
            int currentY = yPositionOnScreen + 140;
            
            var providers = new[] { AIProvider.Player2, AIProvider.Ollama, AIProvider.OpenRouter, AIProvider.Google, AIProvider.OpenAI };
            foreach (var provider in providers)
            {
                providerOptions.Add(new ClickableComponent(
                    new Rectangle(leftColumnX, currentY, 280, 38), provider.ToString())
                { myID = (int)provider });
                currentY += 45;
            }
            
            detectPlayer2Button = new ClickableTextureComponent(
                new Rectangle(rightColumnX, yPositionOnScreen + 140, 460, 42), null, Rectangle.Empty, 1f);
            deviceAuthButton = new ClickableTextureComponent(
                new Rectangle(rightColumnX, yPositionOnScreen + 200, 460, 42), null, Rectangle.Empty, 1f);
            cancelDeviceAuthButton = new ClickableTextureComponent(
                new Rectangle(rightColumnX, yPositionOnScreen + 260, 200, 36), null, Rectangle.Empty, 1f);
            apiKeyInputBox = new ClickableTextureComponent(
                new Rectangle(rightColumnX, yPositionOnScreen + 350, 460, 45), null, Rectangle.Empty, 1f);
            testConnectionButton = new ClickableTextureComponent(
                new Rectangle(rightColumnX, yPositionOnScreen + 410, 220, 42), null, Rectangle.Empty, 1f);
            fetchModelsButton = new ClickableTextureComponent(
                new Rectangle(rightColumnX + 230, yPositionOnScreen + 410, 230, 42), null, Rectangle.Empty, 1f);
            modelSearchBox = new ClickableTextureComponent(
                new Rectangle(rightColumnX, yPositionOnScreen + 470, 460, 45), null, Rectangle.Empty, 1f);
            modelScrollUpButton = new ClickableTextureComponent(
                new Rectangle(rightColumnX + 465, yPositionOnScreen + 530, 35, 35), null, Rectangle.Empty, 1f);
            modelScrollDownButton = new ClickableTextureComponent(
                new Rectangle(rightColumnX + 465, yPositionOnScreen + 570, 35, 35), null, Rectangle.Empty, 1f);
            
            UpdateModelOptions();
        }
        
        private void LoadCurrentSettings()
        {
            var config = ModEntry.ConfigManager;
            if (config != null)
            {
                if (config.Config.Language != "Auto")
                    selectedLanguage = config.Config.Language;
                else if (ModEntry.LanguageManager != null)
                    selectedLanguage = ModEntry.LanguageManager.GetCurrentLanguage();
                
                selectedKey = config.GetChatButton();
                selectedProvider = config.GetSelectedProvider();
                currentApiKey = config.GetApiKeyForProvider(selectedProvider);
                currentModel = config.GetSelectedModelForProvider(selectedProvider);
                playerNickname = config.GetPlayerNickname();
            }
        }
        
        private void UpdatePlayer2StatusDisplay()
        {
            if (selectedProvider != AIProvider.Player2) return;
            if (isDoingDeviceAuth) return;
            
            if (UnifiedAIClient.IsLocalPlayer2Detected)
                connectionStatus = " Player2 app connected!";
            else
            {
                var statusMsg = UnifiedAIClient.LocalDetectionStatusMessage;
                connectionStatus = (!string.IsNullOrEmpty(statusMsg) && statusMsg != "Not checked yet")
                    ? $"(i) {statusMsg}"
                    : "";
            }
        }
        
        private void UpdateModelOptions()
        {
            modelOptions.Clear();
            if (modelSearchBox == null) return;
            
            var filteredModels = availableModels
                .Where(m => string.IsNullOrEmpty(modelSearchText) || 
                           m.Name.Contains(modelSearchText, StringComparison.OrdinalIgnoreCase) ||
                           m.Id.Contains(modelSearchText, StringComparison.OrdinalIgnoreCase))
                .Skip(modelScrollOffset)
                .Take(MAX_VISIBLE_MODELS)
                .ToList();
            
            int yOffset = modelSearchBox.bounds.Y + 60;
            foreach (var model in filteredModels)
            {
                modelOptions.Add(new ClickableComponent(
                    new Rectangle(modelSearchBox.bounds.X, yOffset, 460, 35), model.Id)
                { label = model.GetDisplayName() });
                yOffset += 38;
            }
        }
        
        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);
            
            foreach (var tab in tabButtons)
            {
                if (tab.containsPoint(x, y))
                {
                    currentTab = tab.name == "General" ? SettingsTab.General : SettingsTab.AIProvider;
                    if (currentTab == SettingsTab.AIProvider)
                        UpdatePlayer2StatusDisplay();
                    Game1.playSound("smallSelect");
                    return;
                }
            }
            
            if (currentTab == SettingsTab.General)
                HandleGeneralTabClick(x, y);
            else if (currentTab == SettingsTab.AIProvider)
                HandleAIProviderTabClick(x, y);
            
            if (saveButton != null && saveButton.containsPoint(x, y))
            {
                SaveSettings();
                CloseModal();
                return;
            }
            
            if (cancelButton != null && cancelButton.containsPoint(x, y))
            {
                CloseModal();
                return;
            }
        }
        
        private void HandleGeneralTabClick(int x, int y)
        {
            foreach (var option in languageOptions)
            {
                if (option.containsPoint(x, y))
                {
                    selectedLanguage = option.name;
                    Game1.playSound("smallSelect");
                    return;
                }
            }
            
            var keyRemapArea = new Rectangle(xPositionOnScreen + 500, yPositionOnScreen + 158, 300, 45);
            if (keyRemapArea.Contains(x, y))
            {
                isWaitingForKeyPress = true;
                isEditingPlayerName = false;
                Game1.keyboardDispatcher.Subscriber = null;
                Game1.playSound("smallSelect");
                return;
            }
            
            if (resetKeyButton != null && resetKeyButton.containsPoint(x, y))
            {
                selectedKey = SButton.MouseLeft;
                isWaitingForKeyPress = false;
                Game1.playSound("drumkit6");
                return;
            }

            if (playerNameInputBox != null && playerNameInputBox.containsPoint(x, y))
            {
                isEditingPlayerName = true;
                isWaitingForKeyPress = false;
                Game1.keyboardDispatcher.Subscriber = new PlayerNameTextReceiver(this);
                Game1.playSound("smallSelect");
                return;
            }

            //  Botón que abre GlobalInstructionsModal
            if (globalInstructionsButton != null && globalInstructionsButton.containsPoint(x, y))
            {
                // Guardar player name antes de abrir la ventana
                if (ModEntry.ConfigManager != null)
                    ModEntry.ConfigManager.SetPlayerNickname(playerNickname);

                if (isEditingPlayerName)
                {
                    isEditingPlayerName = false;
                    Game1.keyboardDispatcher.Subscriber = null;
                }

                Game1.activeClickableMenu = new GlobalInstructionsModal(this, monitor);
                Game1.playSound("smallSelect");
                return;
            }

            // Click en otro lugar: cerrar inputs abiertos
            if (isEditingPlayerName)
            {
                isEditingPlayerName = false;
                Game1.keyboardDispatcher.Subscriber = null;
            }
        }
        
        private void HandleAIProviderTabClick(int x, int y)
        {
            foreach (var option in providerOptions)
            {
                if (option.containsPoint(x, y))
                {
                    var newProvider = (AIProvider)option.myID;
                    if (selectedProvider != newProvider)
                    {
                        CancelDeviceAuth();
                        selectedProvider = newProvider;
                        if (ModEntry.ConfigManager != null)
                        {
                            currentApiKey = ModEntry.ConfigManager.GetApiKeyForProvider(selectedProvider);
                            currentModel = ModEntry.ConfigManager.GetSelectedModelForProvider(selectedProvider);
                            modelSearchText = (selectedProvider != AIProvider.Player2 && !string.IsNullOrEmpty(currentModel))
                                ? currentModel : "";
                        }
                        availableModels.Clear();
                        modelScrollOffset = 0;
                        connectionStatus = "";
                        UpdateModelOptions();
                        if (selectedProvider == AIProvider.Player2)
                            UpdatePlayer2StatusDisplay();
                        Game1.playSound("smallSelect");
                    }
                    return;
                }
            }
            
            if (selectedProvider == AIProvider.Player2)
            {
                if (detectPlayer2Button != null && detectPlayer2Button.containsPoint(x, y) && !isDetectingPlayer2 && !isDoingDeviceAuth)
                { DetectPlayer2App(); return; }
                if (deviceAuthButton != null && deviceAuthButton.containsPoint(x, y) && !isDetectingPlayer2 && !isDoingDeviceAuth)
                { StartDeviceCodeAuth(); return; }
                if (cancelDeviceAuthButton != null && cancelDeviceAuthButton.containsPoint(x, y) && isDoingDeviceAuth)
                { CancelDeviceAuth(); return; }
            }
            
            if (apiKeyInputBox != null && apiKeyInputBox.containsPoint(x, y))
            {
                isEditingApiKey = true; isEditingModelSearch = false;
                Game1.keyboardDispatcher.Subscriber = new APIKeyTextReceiver(this);
                Game1.playSound("smallSelect"); return;
            }
            if (modelSearchBox != null && modelSearchBox.containsPoint(x, y))
            {
                isEditingModelSearch = true; isEditingApiKey = false;
                Game1.keyboardDispatcher.Subscriber = new ModelSearchTextReceiver(this);
                Game1.playSound("smallSelect"); return;
            }
            if (testConnectionButton != null && testConnectionButton.containsPoint(x, y) && !isTestingConnection)
            { TestConnection(); return; }
            if (fetchModelsButton != null && fetchModelsButton.containsPoint(x, y) && !isFetchingModels)
            { FetchModels(); return; }
            if (modelScrollUpButton != null && modelScrollUpButton.containsPoint(x, y))
            { if (modelScrollOffset > 0) { modelScrollOffset--; UpdateModelOptions(); Game1.playSound("shiny4"); } return; }
            if (modelScrollDownButton != null && modelScrollDownButton.containsPoint(x, y))
            {
                var fc = availableModels.Count(m => string.IsNullOrEmpty(modelSearchText) ||
                    m.Name.Contains(modelSearchText, StringComparison.OrdinalIgnoreCase) ||
                    m.Id.Contains(modelSearchText, StringComparison.OrdinalIgnoreCase));
                if (modelScrollOffset + MAX_VISIBLE_MODELS < fc) { modelScrollOffset++; UpdateModelOptions(); Game1.playSound("shiny4"); }
                return;
            }
            foreach (var option in modelOptions)
            {
                if (option.containsPoint(x, y)) { currentModel = option.name; Game1.playSound("smallSelect"); return; }
            }
        }
        
        // ===== DEVICE CODE OAUTH =====
        
        private async void StartDeviceCodeAuth()
        {
            isDoingDeviceAuth = true;
            deviceUserCode = "";
            devicePollCode = "";
            connectionStatus = " Requesting authorization code...";
            Game1.playSound("smallSelect");
            deviceAuthCts = new CancellationTokenSource();
            var token = deviceAuthCts.Token;
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var body = new StringContent($"{{\"client_id\":\"{GameClientId}\"}}", Encoding.UTF8, "application/json");
                var response = await client.PostAsync($"{Player2WebApiUrl}/login/device/new", body);
                if (!response.IsSuccessStatusCode)
                { connectionStatus = $"[Error] Failed to start auth ({(int)response.StatusCode}). Try again."; isDoingDeviceAuth = false; return; }
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                devicePollCode = json["device_code"]?.ToString() ?? json["deviceCode"]?.ToString() ?? "";
                deviceUserCode = json["user_code"]?.ToString() ?? json["userCode"]?.ToString() ?? "";
                devicePollInterval = json["interval"]?.ToObject<int>() ?? 5;
                var verificationUrl = json["verification_uri_complete"]?.ToString()
                                   ?? json["verificationUriComplete"]?.ToString()
                                   ?? json["verification_uri"]?.ToString()
                                   ?? json["verificationUri"]?.ToString()
                                   ?? "https://player2.game";
                if (string.IsNullOrEmpty(devicePollCode))
                { connectionStatus = "[Error] Invalid response from Player2. Try again."; isDoingDeviceAuth = false; return; }
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = verificationUrl, UseShellExecute = true });
                    connectionStatus = $" Browser opened. Code: {deviceUserCode}\nWaiting for authorization...";
                }
                catch { connectionStatus = $"(!) Visit: {verificationUrl}\nCode: {deviceUserCode}"; }
                await PollForDeviceToken(token);
            }
            catch (OperationCanceledException) { connectionStatus = "Auth cancelled."; isDoingDeviceAuth = false; }
            catch (Exception ex)
            { monitor.Log($"Device auth error: {ex.Message}", LogLevel.Error); connectionStatus = "[Error] Auth error. Check SMAPI log for details."; isDoingDeviceAuth = false; }
        }
        
        private async Task PollForDeviceToken(CancellationToken token)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var deadline = DateTime.Now.AddMinutes(10);
            while (!token.IsCancellationRequested && DateTime.Now < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(devicePollInterval), token);
                if (token.IsCancellationRequested) break;
                try
                {
                    var pollBody = new StringContent(
                        $"{{\"client_id\":\"{GameClientId}\",\"device_code\":\"{devicePollCode}\",\"grant_type\":\"urn:ietf:params:oauth:grant-type:device_code\"}}",
                        Encoding.UTF8, "application/json");
                    var pollResponse = await client.PostAsync($"{Player2WebApiUrl}/login/device/token", pollBody);
                    if (pollResponse.IsSuccessStatusCode)
                    {
                        var pollJson = JObject.Parse(await pollResponse.Content.ReadAsStringAsync());
                        var p2Key = pollJson["p2Key"]?.ToString() ?? pollJson["access_token"]?.ToString();
                        if (!string.IsNullOrEmpty(p2Key))
                        {
                            currentApiKey = p2Key;
                            if (ModEntry.ConfigManager != null)
                            { ModEntry.ConfigManager.SetApiKeyForProvider(AIProvider.Player2, p2Key); monitor.Log(" Player2 API key obtained via Device Code auth", LogLevel.Info); }
                            connectionStatus = " Signed in! API key saved automatically.";
                            isDoingDeviceAuth = false; deviceUserCode = ""; devicePollCode = "";
                            Game1.playSound("achievement"); return;
                        }
                    }
                    else if ((int)pollResponse.StatusCode == 400) { monitor.Log("Device auth: still waiting for user...", LogLevel.Trace); }
                    else { connectionStatus = $"[Error] Auth failed ({(int)pollResponse.StatusCode}). Try again."; isDoingDeviceAuth = false; return; }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { monitor.Log($"Device auth poll error: {ex.Message}", LogLevel.Warn); }
            }
            if (!token.IsCancellationRequested) { connectionStatus = "[Timeout] Auth timed out. Try signing in again."; isDoingDeviceAuth = false; }
        }
        
        private void CancelDeviceAuth()
        {
            if (deviceAuthCts != null) { deviceAuthCts.Cancel(); deviceAuthCts.Dispose(); deviceAuthCts = null; }
            isDoingDeviceAuth = false; deviceUserCode = ""; devicePollCode = ""; connectionStatus = "";
        }
        
        // ===== DETECT PLAYER2 APP =====
        
        private async void DetectPlayer2App()
        {
            isDetectingPlayer2 = true;
            connectionStatus = " Checking for Player2 app...";
            Game1.playSound("smallSelect");
            await Task.Run(async () =>
            {
                try
                {
                    if (ModEntry.ConfigManager == null || ModEntry.ModHelper == null) { connectionStatus = "[Error] Configuration system not available"; return; }
                    var tempConfig = new ConfigManager(ModEntry.ModHelper, monitor);
                    tempConfig.UpdateProvider(AIProvider.Player2);
                    var tempClient = new UnifiedAIClient(monitor, tempConfig);
                    bool detected = await tempClient.ForceDetectLocalPlayer2Async();
                    if (detected) { connectionStatus = " Player2 app detected and connected!"; Game1.playSound("achievement"); }
                    else
                    {
                        connectionStatus = $"(!) {UnifiedAIClient.LocalDetectionStatusMessage}";
                        if (!string.IsNullOrEmpty(currentApiKey)) connectionStatus += " (using API key)";
                    }
                }
                catch (Exception ex) { monitor.Log($"Player2 detection error: {ex.Message}", LogLevel.Error); connectionStatus = "[Error] Detection failed"; }
                finally { isDetectingPlayer2 = false; }
            });
        }
        
        // ===== TEST CONNECTION =====
        
        private async void TestConnection()
        {
            if (selectedProvider == AIProvider.Player2) { DetectPlayer2App(); return; }
            if (string.IsNullOrEmpty(currentApiKey)) { connectionStatus = "(!) Please enter an API key first"; Game1.playSound("cancel"); return; }
            isTestingConnection = true; connectionStatus = " Testing connection..."; Game1.playSound("smallSelect");
            await Task.Run(async () =>
            {
                try
                {
                    if (ModEntry.ConfigManager == null || ModEntry.ModHelper == null) { connectionStatus = "[Error] Configuration system not available"; isTestingConnection = false; return; }
                    var tempConfigManager = new ConfigManager(ModEntry.ModHelper, monitor);
                    tempConfigManager.UpdateProvider(selectedProvider);
                    tempConfigManager.SetApiKeyForProvider(selectedProvider, currentApiKey);
                    if (!string.IsNullOrEmpty(currentModel)) tempConfigManager.SetSelectedModelForProvider(selectedProvider, currentModel);
                    var testClient = new UnifiedAIClient(monitor, tempConfigManager);
                    var models = await testClient.FetchModelsForProvider(selectedProvider, currentApiKey);
                    if (models != null && models.Count > 0) { connectionStatus = $" Connected! Found {models.Count} models"; availableModels = models; modelScrollOffset = 0; UpdateModelOptions(); }
                    else { connectionStatus = "(!) Connected but no models found"; }
                }
                catch (Exception ex) { monitor.Log($"Connection test failed: {ex.Message}", LogLevel.Error); connectionStatus = "[Error] Connection failed"; }
                finally { isTestingConnection = false; }
            });
        }
        
        // ===== FETCH MODELS =====
        
        private async void FetchModels()
        {
            if (string.IsNullOrEmpty(currentApiKey)) { connectionStatus = "(!) Please enter an API key first"; Game1.playSound("cancel"); return; }
            if (selectedProvider == AIProvider.Player2) { connectionStatus = "(i) Player2 uses its default model"; Game1.playSound("cancel"); return; }
            isFetchingModels = true; connectionStatus = " Fetching models..."; Game1.playSound("smallSelect");
            await Task.Run(async () =>
            {
                try
                {
                    if (ModEntry.ModHelper == null) { connectionStatus = "[Error] Mod helper not available"; isFetchingModels = false; return; }
                    var tempConfigManager = new ConfigManager(ModEntry.ModHelper, monitor);
                    tempConfigManager.UpdateProvider(selectedProvider);
                    tempConfigManager.SetApiKeyForProvider(selectedProvider, currentApiKey);
                    var testClient = new UnifiedAIClient(monitor, tempConfigManager);
                    var models = await testClient.FetchModelsForProvider(selectedProvider, currentApiKey);
                    if (models != null && models.Count > 0) { availableModels = models; modelScrollOffset = 0; UpdateModelOptions(); connectionStatus = $" Fetched {models.Count} models"; }
                    else { connectionStatus = "(!) No models found"; availableModels.Clear(); UpdateModelOptions(); }
                }
                catch (Exception ex) { monitor.Log($"Error fetching models: {ex.Message}", LogLevel.Error); connectionStatus = "[Error] Failed to fetch models"; availableModels.Clear(); UpdateModelOptions(); }
                finally { isFetchingModels = false; }
            });
        }
        
        // ===== KEY INPUT =====
        
        public override void receiveKeyPress(Keys key)
        {
            if (isEditingPlayerName)
            {
                if (key == Keys.Escape) { isEditingPlayerName = false; Game1.keyboardDispatcher.Subscriber = null; }
                return;
            }

            if (isEditingApiKey || isEditingModelSearch)
            {
                if (key == Keys.Escape) { isEditingApiKey = false; isEditingModelSearch = false; Game1.keyboardDispatcher.Subscriber = null; }
                return;
            }
            
            if (currentTab == SettingsTab.General && isWaitingForKeyPress)
            {
                if (Enum.TryParse<SButton>(key.ToString(), out SButton sButton)) { selectedKey = sButton; isWaitingForKeyPress = false; Game1.playSound("coin"); }
                return;
            }
            
            if (key == Keys.Escape) { CloseModal(); return; }
            
            base.receiveKeyPress(key);
        }

        // ===== SAVE & CLOSE =====

        private void SaveSettings()
        {
            var configManager = ModEntry.ConfigManager;
            if (configManager != null)
            {
                if (FontManager.RequiresGameRestart(selectedLanguage))
                {
                    string warningMessage = selectedLanguage switch
                    {
                        "Chinese" => "To display Chinese characters, please restart the game.",
                        "Japanese" => "To display Japanese characters, please restart the game.",
                        "Korean" => "To display Korean characters, please restart the game.",
                        _ => "This language requires restarting the game."
                    };
                    Game1.addHUDMessage(new HUDMessage(warningMessage, HUDMessage.error_type));
                }
                configManager.UpdateLanguage(selectedLanguage);
                configManager.UpdateChatKey(selectedKey);
                if (ModEntry.LanguageManager != null) ModEntry.LanguageManager.SetLanguage(selectedLanguage);
                configManager.SetPlayerNickname(playerNickname);
                configManager.UpdateProvider(selectedProvider);
                configManager.SetApiKeyForProvider(selectedProvider, currentApiKey);
                if (selectedProvider != AIProvider.Player2 || !string.IsNullOrEmpty(currentModel))
                    configManager.SetSelectedModelForProvider(selectedProvider, currentModel);
                Game1.addHUDMessage(new HUDMessage("Settings saved!", HUDMessage.achievement_type));
            }
            Game1.playSound("bigSelect");
        }
        
        private void CloseModal()
        {
            CancelDeviceAuth();
            if (isEditingApiKey || isEditingModelSearch || isEditingPlayerName)
                Game1.keyboardDispatcher.Subscriber = null;
            Game1.activeClickableMenu = parentChatUI;
            Game1.playSound("bigDeSelect");
        }
        
        // ===== DRAW =====
        
        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.7f);
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                xPositionOnScreen, yPositionOnScreen, modalWidth, modalHeight, backgroundColor);
            string title = "Settings";
            Vector2 titleSize = Game1.dialogueFont.MeasureString(title);
            Utility.drawTextWithShadow(b, title, Game1.dialogueFont,
                new Vector2(xPositionOnScreen + (modalWidth - titleSize.X) / 2, yPositionOnScreen + 25), Color.White);
            DrawTabs(b);
            if (currentTab == SettingsTab.General) DrawGeneralTab(b);
            else if (currentTab == SettingsTab.AIProvider) DrawAIProviderTab(b);
            DrawButton(b, saveButton, "Save", Color.Green);
            DrawButton(b, cancelButton, "Cancel", Color.Red);
            drawMouse(b);
        }
        
        private void DrawTabs(SpriteBatch b)
        {
            foreach (var tab in tabButtons)
            {
                bool isActive = (tab.name == "General" && currentTab == SettingsTab.General) ||
                               (tab.name == "AI Provider" && currentTab == SettingsTab.AIProvider);
                Color tabColor = isActive ? new Color(80, 80, 120) : new Color(60, 60, 60);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    tab.bounds.X, tab.bounds.Y, tab.bounds.Width, tab.bounds.Height, tabColor);
                Vector2 textSize = Game1.smallFont.MeasureString(tab.name);
                Utility.drawTextWithShadow(b, tab.name, Game1.smallFont,
                    new Vector2(tab.bounds.X + (tab.bounds.Width - textSize.X) / 2,
                            tab.bounds.Y + (tab.bounds.Height - textSize.Y) / 2),
                    isActive ? Color.Yellow : Color.White);
            }
        }
        
        private void DrawGeneralTab(SpriteBatch b)
        {
            Utility.drawTextWithShadow(b, "Language:", Game1.smallFont,
                new Vector2(xPositionOnScreen + 50, yPositionOnScreen + 130), Color.Yellow);
            
            foreach (var option in languageOptions)
            {
                Color optionColor = option.name == selectedLanguage ? Color.Green : Color.White;
                Color bgColor = option.name == selectedLanguage ? new Color(50, 100, 50, 200) : new Color(60, 60, 60, 200);
                b.Draw(Game1.fadeToBlackRect, option.bounds, bgColor);
                if (option.name == selectedLanguage) DrawBorder(b, option.bounds, Color.Green, 2);
                Utility.drawTextWithShadow(b, option.name, Game1.smallFont,
                    new Vector2(option.bounds.X + 15, option.bounds.Y + 8), optionColor);
            }

            if ((selectedLanguage == "Chinese" || selectedLanguage == "Japanese" || selectedLanguage == "Korean") &&
                !FontManager.IsCJKFontAvailable(selectedLanguage))
            {
                Utility.drawTextWithShadow(b, "* Your game must be set to this language to display characters correctly.", Game1.smallFont,
                    new Vector2(xPositionOnScreen + 50, yPositionOnScreen + modalHeight - 100), Color.Orange);
                Utility.drawTextWithShadow(b, "  NPCs will still respond in the selected language.", Game1.smallFont,
                    new Vector2(xPositionOnScreen + 50, yPositionOnScreen + modalHeight - 78), new Color(200, 180, 100));
            }
            
            int rightX = xPositionOnScreen + 500;

            // ── Open Chat Key ──
            Utility.drawTextWithShadow(b, "Open Chat Key:", Game1.smallFont,
                new Vector2(rightX, yPositionOnScreen + 130), Color.Yellow);
            var keyBox = new Rectangle(rightX, yPositionOnScreen + 158, 300, 45);
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                keyBox.X, keyBox.Y, keyBox.Width, keyBox.Height,
                isWaitingForKeyPress ? new Color(100, 200, 100) : new Color(60, 60, 60));
            string keyText = isWaitingForKeyPress ? "Press any key..." : selectedKey.ToString();
            Vector2 keyTextSize = Game1.smallFont.MeasureString(keyText);
            Utility.drawTextWithShadow(b, keyText, Game1.smallFont,
                new Vector2(keyBox.X + (keyBox.Width - keyTextSize.X) / 2,
                        keyBox.Y + (keyBox.Height - keyTextSize.Y) / 2),
                isWaitingForKeyPress ? Color.Yellow : Color.White);
            if (resetKeyButton != null)
            {
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    resetKeyButton.bounds.X, resetKeyButton.bounds.Y,
                    resetKeyButton.bounds.Width, resetKeyButton.bounds.Height, new Color(200, 100, 100) * 0.8f);
                string resetText = "Reset";
                Vector2 resetSize = Game1.smallFont.MeasureString(resetText);
                Utility.drawTextWithShadow(b, resetText, Game1.smallFont,
                    new Vector2(resetKeyButton.bounds.X + (resetKeyButton.bounds.Width - resetSize.X) / 2,
                            resetKeyButton.bounds.Y + (resetKeyButton.bounds.Height - resetSize.Y) / 2), Color.White);
            }

            // ── Player Name ── (Reset termina en y+255, +115px → y+370)
            Utility.drawTextWithShadow(b, "Player Name (for NPCs):", Game1.smallFont,
                new Vector2(rightX, yPositionOnScreen + 370), Color.Yellow);
            if (playerNameInputBox != null)
            {
                playerNameInputBox = new ClickableTextureComponent(
                    new Rectangle(rightX, yPositionOnScreen + 400, 280, 40), null, Rectangle.Empty, 1f);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    playerNameInputBox.bounds.X, playerNameInputBox.bounds.Y,
                    playerNameInputBox.bounds.Width, playerNameInputBox.bounds.Height,
                    isEditingPlayerName ? new Color(100, 200, 100) : new Color(60, 60, 60));
                string displayName = string.IsNullOrEmpty(playerNickname)
                    ? (isEditingPlayerName ? "|" : "Auto (uses in-game name)")
                    : (isEditingPlayerName ? playerNickname + "|" : playerNickname);
                if (displayName.Length > 28) displayName = displayName.Substring(0, 25) + "...";
                Utility.drawTextWithShadow(b, displayName, Game1.smallFont,
                    new Vector2(playerNameInputBox.bounds.X + 10, playerNameInputBox.bounds.Y + 11),
                    isEditingPlayerName ? Color.Yellow
                        : (string.IsNullOrEmpty(playerNickname) ? new Color(120, 120, 120) : Color.White));
            }

            // ── Global Instructions button ── (input termina en y+440, +100px → y+540)
            Utility.drawTextWithShadow(b, "Global Instructions:", Game1.smallFont,
                new Vector2(rightX, yPositionOnScreen + 540), Color.Yellow);
            if (globalInstructionsButton != null)
            {
                globalInstructionsButton = new ClickableTextureComponent(
                    new Rectangle(rightX, yPositionOnScreen + 568, 280, 42), null, Rectangle.Empty, 1f);

                // Mostrar preview del contenido actual si existe
                string currentInstructions = ModEntry.ConfigManager?.GetGlobalInstructions() ?? "";
                bool hasInstructions = !string.IsNullOrWhiteSpace(currentInstructions);

                Color btnColor = hasInstructions ? new Color(80, 120, 80) : new Color(60, 80, 120);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    globalInstructionsButton.bounds.X, globalInstructionsButton.bounds.Y,
                    globalInstructionsButton.bounds.Width, globalInstructionsButton.bounds.Height,
                    btnColor * 0.9f);

                string btnText = hasInstructions ? " Edit Global Instructions" : "+ Add Global Instructions";
                Vector2 btnSize = Game1.smallFont.MeasureString(btnText);
                Utility.drawTextWithShadow(b, btnText, Game1.smallFont,
                    new Vector2(globalInstructionsButton.bounds.X + (globalInstructionsButton.bounds.Width - btnSize.X) / 2,
                            globalInstructionsButton.bounds.Y + (globalInstructionsButton.bounds.Height - btnSize.Y) / 2),
                    Color.White);

                // Preview del texto actual (truncado)
                if (hasInstructions)
                {
                    string preview = currentInstructions.Length > 45
                        ? currentInstructions.Substring(0, 42) + "..."
                        : currentInstructions;
                    Utility.drawTextWithShadow(b, preview, Game1.tinyFont,
                        new Vector2(globalInstructionsButton.bounds.X + 5,
                                globalInstructionsButton.bounds.Bottom + 4),
                        new Color(150, 200, 150));
                }
            }
        }
        
        private void DrawAIProviderTab(SpriteBatch b)
        {
            int leftColumnX = xPositionOnScreen + 50;
            int rightColumnX = xPositionOnScreen + 380;
            Utility.drawTextWithShadow(b, "AI Provider:", Game1.smallFont,
                new Vector2(leftColumnX, yPositionOnScreen + 125), Color.Yellow);
            foreach (var option in providerOptions)
            {
                var provider = (AIProvider)option.myID;
                bool isSelected = provider == selectedProvider;
                Color bgColor = isSelected ? new Color(50, 100, 50, 200) : new Color(60, 60, 60, 200);
                b.Draw(Game1.fadeToBlackRect, option.bounds, bgColor);
                if (isSelected) DrawBorder(b, option.bounds, Color.Green, 2);
                string displayName = provider.GetLabel();
                if (provider == AIProvider.Player2 && UnifiedAIClient.IsLocalPlayer2Detected) displayName += " ✓";
                Utility.drawTextWithShadow(b, displayName, Game1.smallFont,
                    new Vector2(option.bounds.X + 15, option.bounds.Y + 10),
                    isSelected ? Color.Green : Color.White);
            }
            int currentY = yPositionOnScreen + 125;
            if (selectedProvider == AIProvider.Player2) DrawPlayer2Section(b, rightColumnX, ref currentY);
            else DrawStandardProviderSection(b, rightColumnX, ref currentY);
        }
        
        private void DrawPlayer2Section(SpriteBatch b, int rightColumnX, ref int currentY)
        {
            bool isDetected = UnifiedAIClient.IsLocalPlayer2Detected;
            bool hasApiKey = !string.IsNullOrEmpty(currentApiKey);
            bool isWorking = isDetected || hasApiKey;
            var statusBox = new Rectangle(rightColumnX, currentY, 460, 50);
            Color statusBgColor = isWorking ? new Color(30, 100, 30, 220) : new Color(80, 60, 20, 220);
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                statusBox.X, statusBox.Y, statusBox.Width, statusBox.Height, statusBgColor);
            string statusText = isDetected ? "Player2 App Connected!"
                              : (hasApiKey ? " Using API Key" : "(!) Player2 not configured");
            Color statusColor = isDetected ? Color.LightGreen
                              : (hasApiKey ? new Color(150, 220, 255) : new Color(255, 200, 100));
            Vector2 statusSize = Game1.smallFont.MeasureString(statusText);
            Utility.drawTextWithShadow(b, statusText, Game1.smallFont,
                new Vector2(statusBox.X + (statusBox.Width - statusSize.X) / 2,
                        statusBox.Y + (statusBox.Height - statusSize.Y) / 2), statusColor);
            currentY += 60;
            if (detectPlayer2Button != null)
            {
                detectPlayer2Button = new ClickableTextureComponent(new Rectangle(rightColumnX, currentY, 460, 38), null, Rectangle.Empty, 1f);
                bool disabled = isDetectingPlayer2 || isDoingDeviceAuth;
                Color btnColor = disabled ? new Color(60, 60, 60) : new Color(70, 120, 70);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    detectPlayer2Button.bounds.X, detectPlayer2Button.bounds.Y,
                    detectPlayer2Button.bounds.Width, detectPlayer2Button.bounds.Height, btnColor * 0.9f);
                string btnText = isDetectingPlayer2 ? " Checking..." : (isDetected ? " Re-check Player2 App" : " Check for Player2 App");
                Vector2 btnSize = Game1.smallFont.MeasureString(btnText);
                Utility.drawTextWithShadow(b, btnText, Game1.smallFont,
                    new Vector2(detectPlayer2Button.bounds.X + (detectPlayer2Button.bounds.Width - btnSize.X) / 2,
                            detectPlayer2Button.bounds.Y + (detectPlayer2Button.bounds.Height - btnSize.Y) / 2),
                    disabled ? Color.Gray : Color.White);
            }
            currentY += 48;
            DrawSeparator(b, rightColumnX, currentY, "── Or sign in / use API key ──");
            currentY += 28;
            if (!isDoingDeviceAuth)
            {
                if (deviceAuthButton != null)
                {
                    deviceAuthButton = new ClickableTextureComponent(new Rectangle(rightColumnX, currentY, 460, 42), null, Rectangle.Empty, 1f);
                    bool disabled = isDetectingPlayer2;
                    Color btnColor = disabled ? new Color(50, 50, 80) : new Color(80, 80, 180);
                    IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                        deviceAuthButton.bounds.X, deviceAuthButton.bounds.Y,
                        deviceAuthButton.bounds.Width, deviceAuthButton.bounds.Height, btnColor * 0.9f);
                    string btnText = " Sign in with Player2 Account";
                    Vector2 btnSize = Game1.smallFont.MeasureString(btnText);
                    Utility.drawTextWithShadow(b, btnText, Game1.smallFont,
                        new Vector2(deviceAuthButton.bounds.X + (deviceAuthButton.bounds.Width - btnSize.X) / 2,
                                deviceAuthButton.bounds.Y + (deviceAuthButton.bounds.Height - btnSize.Y) / 2),
                        disabled ? Color.Gray : Color.White);
                }
                currentY += 52;
            }
            else
            {
                var authBox = new Rectangle(rightColumnX, currentY, 460, 80);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    authBox.X, authBox.Y, authBox.Width, authBox.Height, new Color(40, 40, 80, 220));
                Utility.drawTextWithShadow(b, " Complete sign-in in your browser", Game1.smallFont, new Vector2(authBox.X + 15, authBox.Y + 12), Color.LightBlue);
                Utility.drawTextWithShadow(b, !string.IsNullOrEmpty(deviceUserCode) ? $"Code: {deviceUserCode}" : "Waiting...", Game1.smallFont, new Vector2(authBox.X + 15, authBox.Y + 36), Color.Yellow);
                if (cancelDeviceAuthButton != null)
                {
                    cancelDeviceAuthButton = new ClickableTextureComponent(new Rectangle(authBox.Right - 110, authBox.Y + 22, 100, 30), null, Rectangle.Empty, 1f);
                    IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                        cancelDeviceAuthButton.bounds.X, cancelDeviceAuthButton.bounds.Y,
                        cancelDeviceAuthButton.bounds.Width, cancelDeviceAuthButton.bounds.Height, new Color(150, 50, 50) * 0.9f);
                    string cancelText = "Cancel";
                    Vector2 cSize = Game1.smallFont.MeasureString(cancelText);
                    Utility.drawTextWithShadow(b, cancelText, Game1.smallFont,
                        new Vector2(cancelDeviceAuthButton.bounds.X + (cancelDeviceAuthButton.bounds.Width - cSize.X) / 2,
                                cancelDeviceAuthButton.bounds.Y + (cancelDeviceAuthButton.bounds.Height - cSize.Y) / 2), Color.White);
                }
                currentY += 92;
            }
            Utility.drawTextWithShadow(b, "API Key:", Game1.smallFont, new Vector2(rightColumnX, currentY), new Color(200, 200, 200));
            currentY += 22;
            if (apiKeyInputBox != null)
            {
                apiKeyInputBox = new ClickableTextureComponent(new Rectangle(rightColumnX, currentY, 460, 40), null, Rectangle.Empty, 1f);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    apiKeyInputBox.bounds.X, apiKeyInputBox.bounds.Y, apiKeyInputBox.bounds.Width, apiKeyInputBox.bounds.Height,
                    isEditingApiKey ? new Color(100, 200, 100) : new Color(50, 50, 50));
                string displayKey = string.IsNullOrEmpty(currentApiKey) ? (isEditingApiKey ? "|" : "Click to enter API key...") :
                    (isEditingApiKey ? currentApiKey + "|" : new string('•', Math.Min(currentApiKey.Length, 50)));
                if (displayKey.Length > 55) displayKey = displayKey.Substring(0, 52) + "...";
                Utility.drawTextWithShadow(b, displayKey, Game1.smallFont,
                    new Vector2(apiKeyInputBox.bounds.X + 12, apiKeyInputBox.bounds.Y + 11),
                    isEditingApiKey ? Color.Yellow : new Color(180, 180, 180));
            }
            currentY += 50;
            if (!string.IsNullOrEmpty(connectionStatus))
            {
                var lines = connectionStatus.Split('\n');
                foreach (var line in lines.Take(2))
                {
                    string truncated = line.Length > 58 ? line.Substring(0, 55) + "..." : line;
                    Color lineColor = line.StartsWith("") ? Color.LightGreen : line.StartsWith("[Error]") ? new Color(255, 120, 120) : line.StartsWith("(!)") ? new Color(255, 210, 100) : Color.LightBlue;
                    Utility.drawTextWithShadow(b, truncated, Game1.smallFont, new Vector2(rightColumnX, currentY), lineColor);
                    currentY += 22;
                }
            }
        }
        
        private void DrawSeparator(SpriteBatch b, int x, int y, string text)
        {
            Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(x + 50, y + 5), new Color(130, 130, 130));
        }
        
        private void DrawStandardProviderSection(SpriteBatch b, int rightColumnX, ref int currentY)
        {
            Utility.drawTextWithShadow(b, "API Key:", Game1.smallFont, new Vector2(rightColumnX, currentY), Color.Yellow);
            currentY += 25;
            if (apiKeyInputBox != null)
            {
                apiKeyInputBox = new ClickableTextureComponent(new Rectangle(rightColumnX, currentY, 460, 45), null, Rectangle.Empty, 1f);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    apiKeyInputBox.bounds.X, apiKeyInputBox.bounds.Y, apiKeyInputBox.bounds.Width, apiKeyInputBox.bounds.Height,
                    isEditingApiKey ? new Color(100, 200, 100) : new Color(60, 60, 60));
                string displayKey = string.IsNullOrEmpty(currentApiKey) ? (isEditingApiKey ? "|" : "Click to enter API key...") :
                    (isEditingApiKey ? currentApiKey + "|" : new string('•', Math.Min(currentApiKey.Length, 50)));
                if (displayKey.Length > 55) displayKey = displayKey.Substring(0, 52) + "...";
                Utility.drawTextWithShadow(b, displayKey, Game1.smallFont,
                    new Vector2(apiKeyInputBox.bounds.X + 15, apiKeyInputBox.bounds.Y + 13),
                    isEditingApiKey ? Color.Yellow : Color.White);
            }
            currentY = (apiKeyInputBox?.bounds.Y ?? currentY) + 60;
            if (testConnectionButton != null)
            {
                testConnectionButton = new ClickableTextureComponent(new Rectangle(rightColumnX, currentY, 220, 42), null, Rectangle.Empty, 1f);
                Color buttonColor = isTestingConnection ? Color.Gray : new Color(100, 150, 200);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    testConnectionButton.bounds.X, testConnectionButton.bounds.Y, testConnectionButton.bounds.Width, testConnectionButton.bounds.Height, buttonColor * 0.8f);
                string buttonText = isTestingConnection ? "Testing..." : "Test Connection";
                Vector2 textSize = Game1.smallFont.MeasureString(buttonText);
                Utility.drawTextWithShadow(b, buttonText, Game1.smallFont,
                    new Vector2(testConnectionButton.bounds.X + (testConnectionButton.bounds.Width - textSize.X) / 2,
                            testConnectionButton.bounds.Y + (testConnectionButton.bounds.Height - textSize.Y) / 2), Color.White);
            }
            if (fetchModelsButton != null)
            {
                fetchModelsButton = new ClickableTextureComponent(new Rectangle(rightColumnX + 230, currentY, 230, 42), null, Rectangle.Empty, 1f);
                Color buttonColor = isFetchingModels ? Color.Gray : new Color(100, 200, 150);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    fetchModelsButton.bounds.X, fetchModelsButton.bounds.Y, fetchModelsButton.bounds.Width, fetchModelsButton.bounds.Height, buttonColor * 0.8f);
                string buttonText = isFetchingModels ? "Fetching..." : "Fetch Models";
                Vector2 textSize = Game1.smallFont.MeasureString(buttonText);
                Utility.drawTextWithShadow(b, buttonText, Game1.smallFont,
                    new Vector2(fetchModelsButton.bounds.X + (fetchModelsButton.bounds.Width - textSize.X) / 2,
                            fetchModelsButton.bounds.Y + (fetchModelsButton.bounds.Height - textSize.Y) / 2), Color.White);
            }
            currentY = (testConnectionButton?.bounds.Y ?? currentY) + 60;
            if (!string.IsNullOrEmpty(connectionStatus))
            {
                Color lineColor = connectionStatus.StartsWith("") ? Color.LightGreen : connectionStatus.StartsWith("[Error]") ? new Color(255, 120, 120) : Color.LightBlue;
                string truncated = connectionStatus.Length > 60 ? connectionStatus.Substring(0, 57) + "..." : connectionStatus;
                Utility.drawTextWithShadow(b, truncated, Game1.smallFont, new Vector2(rightColumnX, currentY), lineColor);
            }
            currentY += 30;
            Utility.drawTextWithShadow(b, "Model Search:", Game1.smallFont, new Vector2(rightColumnX, currentY), Color.Yellow);
            currentY += 25;
            if (modelSearchBox != null)
            {
                modelSearchBox = new ClickableTextureComponent(new Rectangle(rightColumnX, currentY, 460, 45), null, Rectangle.Empty, 1f);
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    modelSearchBox.bounds.X, modelSearchBox.bounds.Y, modelSearchBox.bounds.Width, modelSearchBox.bounds.Height,
                    isEditingModelSearch ? new Color(100, 200, 100) : new Color(60, 60, 60));
                string searchDisplay = string.IsNullOrEmpty(modelSearchText) ? (isEditingModelSearch ? "|" : "Search models...") :
                    (isEditingModelSearch ? modelSearchText + "|" : modelSearchText);
                if (searchDisplay.Length > 55) searchDisplay = searchDisplay.Substring(0, 52) + "...";
                Utility.drawTextWithShadow(b, searchDisplay, Game1.smallFont,
                    new Vector2(modelSearchBox.bounds.X + 15, modelSearchBox.bounds.Y + 13),
                    isEditingModelSearch ? Color.Yellow : Color.White);
            }
            currentY = (modelSearchBox?.bounds.Y ?? currentY) + 60;
            Utility.drawTextWithShadow(b, "Available Models:", Game1.smallFont, new Vector2(rightColumnX, currentY), Color.Yellow);
            currentY += 25;
            if (modelScrollUpButton != null)
            {
                modelScrollUpButton = new ClickableTextureComponent(new Rectangle(rightColumnX + 465, currentY, 35, 35), null, Rectangle.Empty, 1f);
                bool canScrollUp = modelScrollOffset > 0;
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    modelScrollUpButton.bounds.X, modelScrollUpButton.bounds.Y, modelScrollUpButton.bounds.Width, modelScrollUpButton.bounds.Height,
                    canScrollUp ? new Color(100, 100, 100) : new Color(50, 50, 50));
                Utility.drawTextWithShadow(b, "▲", Game1.smallFont,
                    new Vector2(modelScrollUpButton.bounds.X + 11, modelScrollUpButton.bounds.Y + 5),
                    canScrollUp ? Color.White : Color.Gray);
            }
            UpdateModelOptions();
            foreach (var option in modelOptions)
            {
                bool isSelected = option.name == currentModel;
                Color bgColor = isSelected ? new Color(50, 100, 50, 200) : new Color(60, 60, 60, 200);
                b.Draw(Game1.fadeToBlackRect, option.bounds, bgColor);
                if (isSelected) DrawBorder(b, option.bounds, Color.Green, 2);
                string displayText = option.label ?? option.name;
                if (displayText.Length > 55) displayText = displayText.Substring(0, 52) + "...";
                Utility.drawTextWithShadow(b, displayText, Game1.smallFont,
                    new Vector2(option.bounds.X + 12, option.bounds.Y + 8),
                    isSelected ? Color.Green : Color.White);
            }
            if (modelScrollDownButton != null && modelScrollUpButton != null)
            {
                modelScrollDownButton = new ClickableTextureComponent(
                    new Rectangle(rightColumnX + 465, modelScrollUpButton.bounds.Y + 190, 35, 35), null, Rectangle.Empty, 1f);
                var filteredCount = availableModels.Count(m => string.IsNullOrEmpty(modelSearchText) ||
                    m.Name.Contains(modelSearchText, StringComparison.OrdinalIgnoreCase) ||
                    m.Id.Contains(modelSearchText, StringComparison.OrdinalIgnoreCase));
                bool canScrollDown = modelScrollOffset + MAX_VISIBLE_MODELS < filteredCount;
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    modelScrollDownButton.bounds.X, modelScrollDownButton.bounds.Y, modelScrollDownButton.bounds.Width, modelScrollDownButton.bounds.Height,
                    canScrollDown ? new Color(100, 100, 100) : new Color(50, 50, 50));
                Utility.drawTextWithShadow(b, "▼", Game1.smallFont,
                    new Vector2(modelScrollDownButton.bounds.X + 11, modelScrollDownButton.bounds.Y + 5),
                    canScrollDown ? Color.White : Color.Gray);
                if (!string.IsNullOrEmpty(currentModel))
                {
                    string currentModelText = $"Selected: {currentModel}";
                    if (currentModelText.Length > 60) currentModelText = currentModelText.Substring(0, 57) + "...";
                    Utility.drawTextWithShadow(b, currentModelText, Game1.smallFont,
                        new Vector2(rightColumnX, modelScrollDownButton.bounds.Y + 45), Color.LightGreen);
                }
            }
        }
        
        private void DrawButton(SpriteBatch b, ClickableTextureComponent? button, string text, Color color)
        {
            if (button == null) return;
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                button.bounds.X, button.bounds.Y, button.bounds.Width, button.bounds.Height, color * 0.8f);
            Vector2 textSize = Game1.smallFont.MeasureString(text);
            Utility.drawTextWithShadow(b, text, Game1.smallFont,
                new Vector2(button.bounds.X + (button.bounds.Width - textSize.X) / 2,
                        button.bounds.Y + (button.bounds.Height - textSize.Y) / 2), Color.White);
        }
        
        private void DrawBorder(SpriteBatch b, Rectangle rect, Color color, int thickness)
        {
            b.Draw(Game1.fadeToBlackRect, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            b.Draw(Game1.fadeToBlackRect, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
            b.Draw(Game1.fadeToBlackRect, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            b.Draw(Game1.fadeToBlackRect, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
        }
        
        // ===== TEXT INPUT =====
        
        public void ReceiveAPIKeyInput(char inputChar)
        {
            if (char.IsControl(inputChar)) return;
            if (currentApiKey.Length < 200) currentApiKey += inputChar;
        }
        
        public void HandleAPIKeyBackspace()
        {
            if (currentApiKey.Length > 0)
                currentApiKey = currentApiKey.Substring(0, currentApiKey.Length - 1);
        }
        
        public void ReceiveModelSearchInput(char inputChar)
        {
            if (char.IsControl(inputChar)) return;
            if (modelSearchText.Length < 100) { modelSearchText += inputChar; modelScrollOffset = 0; UpdateModelOptions(); }
        }
        
        public void HandleModelSearchBackspace()
        {
            if (modelSearchText.Length > 0) { modelSearchText = modelSearchText.Substring(0, modelSearchText.Length - 1); modelScrollOffset = 0; UpdateModelOptions(); }
        }

        public void ReceivePlayerNameInput(char inputChar)
        {
            if (char.IsControl(inputChar)) return;
            if (playerNickname.Length < 50) playerNickname += inputChar;
        }

        public void HandlePlayerNameBackspace()
        {
            if (playerNickname.Length > 0)
                playerNickname = playerNickname.Substring(0, playerNickname.Length - 1);
        }
    }
    
    public class APIKeyTextReceiver : IKeyboardSubscriber
    {
        private readonly SettingsModal modal;
        public APIKeyTextReceiver(SettingsModal modal) { this.modal = modal; }
        public bool Selected { get; set; } = true;
        public void RecieveTextInput(char inputChar) { if (!char.IsControl(inputChar)) modal.ReceiveAPIKeyInput(inputChar); }
        public void RecieveTextInput(string text) { foreach (char c in text) if (!char.IsControl(c)) modal.ReceiveAPIKeyInput(c); }
        public void RecieveCommandInput(char command) { if (command == '\b') modal.HandleAPIKeyBackspace(); }
        public void RecieveSpecialInput(Keys key) { }
    }
    
    public class ModelSearchTextReceiver : IKeyboardSubscriber
    {
        private readonly SettingsModal modal;
        public ModelSearchTextReceiver(SettingsModal modal) { this.modal = modal; }
        public bool Selected { get; set; } = true;
        public void RecieveTextInput(char inputChar) { if (!char.IsControl(inputChar)) modal.ReceiveModelSearchInput(inputChar); }
        public void RecieveTextInput(string text) { foreach (char c in text) if (!char.IsControl(c)) modal.ReceiveModelSearchInput(c); }
        public void RecieveCommandInput(char command) { if (command == '\b') modal.HandleModelSearchBackspace(); }
        public void RecieveSpecialInput(Keys key) { }
    }

    public class PlayerNameTextReceiver : IKeyboardSubscriber
    {
        private readonly SettingsModal modal;
        public PlayerNameTextReceiver(SettingsModal modal) { this.modal = modal; }
        public bool Selected { get; set; } = true;
        public void RecieveTextInput(char inputChar) { if (!char.IsControl(inputChar)) modal.ReceivePlayerNameInput(inputChar); }
        public void RecieveTextInput(string text) { foreach (char c in text) if (!char.IsControl(c)) modal.ReceivePlayerNameInput(c); }
        public void RecieveCommandInput(char command) { if (command == '\b') modal.HandlePlayerNameBackspace(); }
        public void RecieveSpecialInput(Keys key) { }
    }
}