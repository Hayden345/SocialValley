using StardewModdingAPI;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace SocialValley
{
    public class UnifiedAIClient
    {
        private readonly IMonitor monitor;
        private readonly HttpClient httpClient;
        private readonly ConfigManager configManager;
        
        // ===== CONSTANTES PLAYER2 =====
        private const string GameClientId = "01988480-9aca-751f-8351-cc3c505058ad";
        private const string LocalPlayer2Url = "http://localhost:4315";
        private const string Player2WebApiUrl = "https://api.player2.game/v1";
        
        // ===== CONTANTS OLLAMA =====
        private const string OllamaUrl = "http://localhost:11434/api/v1/chat/completions";

        // ===== ESTADO DE PLAYER2 LOCAL (estático para compartir entre instancias) =====
        private static string? localPlayer2Key = null;
        private static DateTime lastLocalCheck = DateTime.MinValue;
        private static bool isCheckingLocal = false;
        private static string localDetectionStatusMessage = "Not checked yet";

        // ===== PROPIEDADES PÚBLICAS DE ESTADO =====
        public static bool IsLocalPlayer2Detected => localPlayer2Key != null;
        public static string LocalDetectionStatusMessage => localDetectionStatusMessage;
        
        private DateTime lastHealthPing = DateTime.MinValue;
        private int totalAPICallsThisSession = 0;
        private DateTime sessionStartTime = DateTime.Now;

        public UnifiedAIClient(IMonitor monitor, ConfigManager configManager)
        {
            this.monitor = monitor;
            this.configManager = configManager;
            this.httpClient = new HttpClient();
            this.httpClient.Timeout = TimeSpan.FromSeconds(60);
            this.sessionStartTime = DateTime.Now;
        }

        // ===== DETECCIÓN PÚBLICA =====

        public async Task<bool> ForceDetectLocalPlayer2Async()
        {
            localPlayer2Key = null;
            lastLocalCheck = DateTime.MinValue;
            var key = await TryGetLocalPlayer2KeyAsync();
            return !string.IsNullOrEmpty(key);
        }

        public async Task PerformHealthPingIfNeededAsync()
        {
            var provider = configManager.GetSelectedProvider();
            if (provider != AIProvider.Player2) return;
            if ((DateTime.Now - lastHealthPing).TotalSeconds < 60) return;
            
            string? apiKey = !string.IsNullOrEmpty(localPlayer2Key) 
                ? localPlayer2Key 
                : configManager.GetCurrentApiKey();
            
            if (string.IsNullOrEmpty(apiKey)) return;
            
            try
            {
                using var healthClient = new HttpClient();
                healthClient.Timeout = TimeSpan.FromSeconds(5);
                var request = new HttpRequestMessage(HttpMethod.Get, $"{Player2WebApiUrl}/health");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Headers.Add("X-Game-Client-Id", GameClientId);
                var response = await healthClient.SendAsync(request);
                lastHealthPing = DateTime.Now;
                
                if (response.IsSuccessStatusCode)
                {
                    monitor.Log("Player2 health ping OK", LogLevel.Trace);
                }
                else
                {
                    monitor.Log($"Player2 health ping failed: {response.StatusCode}", LogLevel.Debug);
                    if (!string.IsNullOrEmpty(localPlayer2Key))
                    {
                        localPlayer2Key = null;
                        localDetectionStatusMessage = "Session expired, reconnecting...";
                    }
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"Player2 health ping error: {ex.Message}", LogLevel.Trace);
                lastHealthPing = DateTime.Now;
            }
        }

        // ===== DETECCIÓN INTERNA =====

        private async Task<string?> TryGetLocalPlayer2KeyAsync()
        {
            if ((DateTime.Now - lastLocalCheck).TotalSeconds < 60 && localPlayer2Key != null)
                return localPlayer2Key;
            
            if (isCheckingLocal)
                return localPlayer2Key;
            
            isCheckingLocal = true;
            localDetectionStatusMessage = "Checking for Player2 app...";
            
            try
            {
                using var healthClient = new HttpClient();
                healthClient.Timeout = TimeSpan.FromSeconds(3);
                
                // 1. Health check al app local
                try
                {
                    var healthResponse = await healthClient.GetAsync($"{LocalPlayer2Url}/v1/health");
                    if (!healthResponse.IsSuccessStatusCode)
                    {
                        localDetectionStatusMessage = "Player2 app not running";
                        localPlayer2Key = null;
                        lastLocalCheck = DateTime.Now;
                        return null;
                    }
                    monitor.Log("Player2 local app health check passed", LogLevel.Debug);
                }
                catch (TaskCanceledException)
                {
                    localDetectionStatusMessage = "Player2 app not found (timeout)";
                    localPlayer2Key = null;
                    lastLocalCheck = DateTime.Now;
                    return null;
                }
                catch (HttpRequestException)
                {
                    localDetectionStatusMessage = "Player2 app not running";
                    localPlayer2Key = null;
                    lastLocalCheck = DateTime.Now;
                    return null;
                }
                
                // 2. Login para obtener p2Key
                try
                {
                    var loginContent = new StringContent("{}", Encoding.UTF8, "application/json");
                    var loginResponse = await healthClient.PostAsync(
                        $"{LocalPlayer2Url}/v1/login/web/{GameClientId}", 
                        loginContent);
                    
                    if (loginResponse.IsSuccessStatusCode)
                    {
                        var responseContent = await loginResponse.Content.ReadAsStringAsync();
                        var jsonObj = JObject.Parse(responseContent);
                        var p2Key = jsonObj["p2Key"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(p2Key))
                        {
                            localPlayer2Key = p2Key;
                            lastLocalCheck = DateTime.Now;
                            localDetectionStatusMessage = " Player2 app detected and connected!";
                            monitor.Log("✓ Player2 local app authenticated successfully", LogLevel.Info);
                            return p2Key;
                        }
                    }
                    
                    localDetectionStatusMessage = "Player2 app found but login failed — are you logged in?";
                    localPlayer2Key = null;
                    lastLocalCheck = DateTime.Now;
                    return null;
                }
                catch (TaskCanceledException)
                {
                    localDetectionStatusMessage = "Player2 app login timeout";
                    localPlayer2Key = null;
                    lastLocalCheck = DateTime.Now;
                    return null;
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"Local Player2 detection failed: {ex.Message}", LogLevel.Debug);
                localDetectionStatusMessage = "Player2 app not found";
                localPlayer2Key = null;
                lastLocalCheck = DateTime.Now;
                return null;
            }
            finally
            {
                isCheckingLocal = false;
            }
        }

        // ===== MÉTODO PRINCIPAL =====
        
        public async Task<string> GetResponse(StardewValley.NPC npc, string prompt)
        {
            try
            {
                //  FIX: Si el usuario configuró un nombre personalizado, reemplazar el nombre
                // real del juego en el prompt ANTES de enviarlo a la IA. Esto garantiza que
                // aunque ConversationManager use Game1.player.Name directamente, el modelo
                // siempre vea el nombre configurado en Settings.
                string effectiveName = configManager.GetEffectivePlayerName();
                try
                {
                    string inGameName = StardewValley.Game1.player?.Name ?? "";
                    if (!string.IsNullOrEmpty(inGameName) && 
                        !string.IsNullOrEmpty(effectiveName) && 
                        inGameName != effectiveName)
                    {
                        prompt = prompt.Replace(inGameName, effectiveName);
                    }
                }
                catch { /* Si falla el reemplazo, continuar con el prompt original */ }

                var provider = configManager.GetSelectedProvider();
                
                if (provider == AIProvider.Player2)
                {
                    // 1. Intentar Player2 local (desktop app)
                    var localKey = await TryGetLocalPlayer2KeyAsync();
                    if (!string.IsNullOrEmpty(localKey))
                    {
                        monitor.Log("Using local Player2 app", LogLevel.Debug);
                        totalAPICallsThisSession++;
                        return await CallPlayer2API(npc, prompt, localKey, isLocal: true);
                    }
                    
                    // 2. Fallback a API key manual (incluyendo la generada por Device Code)
                    var manualApiKey = configManager.GetCurrentApiKey();
                    if (!string.IsNullOrEmpty(manualApiKey))
                    {
                        monitor.Log("Using manual Player2 API key", LogLevel.Debug);
                        totalAPICallsThisSession++;
                        return await CallPlayer2API(npc, prompt, manualApiKey, isLocal: false);
                    }
                    
                    // 3. Sin configuración
                    return "(!) Player2 not configured. Open the Player2 app or sign in via Settings.";
                }

                if (provider == AIProvider.Ollama)
                {
                    monitor.Log("Using Ollama local API", LogLevel.Debug);
                    totalAPICallsThisSession++;
                    return await CallOllamaAPI(npc, prompt);
                }
                
                // ===== OTROS PROVEEDORES =====
                var apiKey = configManager.GetCurrentApiKey();
                var model = configManager.GetCurrentModel();

                if (!configManager.IsProviderConfigured(provider))
                    return $"(!) {provider.GetLabel()} is not configured. Please set API key and model in Settings.";

                totalAPICallsThisSession++;
                monitor.Log($"API call #{totalAPICallsThisSession} to {npc.Name} using {provider}", LogLevel.Debug);

                return provider switch
                {
                    AIProvider.OpenRouter => await CallOpenRouterAPI(npc, prompt, apiKey, model),
                    AIProvider.Google => await CallGoogleAPI(npc, prompt, apiKey, model),
                    AIProvider.OpenAI => await CallOpenAIAPI(npc, prompt, apiKey, model),
                    _ => "[Error] Invalid AI provider selected"
                };
            }
            catch (Exception ex)
            {
                monitor.Log($"Error in GetResponse: {ex.Message}", LogLevel.Error);
                return "[Error] Unexpected error. Check the SMAPI log for details.";
            }
        }

        // ===== PLAYER2 API =====
        
        private async Task<string> CallPlayer2API(StardewValley.NPC npc, string prompt, string apiKey, bool isLocal)
        {
            try
            {
                var endpoint = $"{Player2WebApiUrl}/chat/completions";
                
                var requestData = new
                {
                    model = "default",
                    messages = new[]
                    {
                        new { role = "system", content = GetNPCSystemPrompt(npc) },
                        new { role = "user", content = prompt }
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json")
                };
                
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Headers.Add("X-Game-Client-Id", GameClientId);

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return ParseOpenAIFormatResponse(responseContent);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    monitor.Log($"Player2 API error: {response.StatusCode} - {errorContent}", LogLevel.Warn);
                    
                    if (isLocal)
                    {
                        localPlayer2Key = null;
                        localDetectionStatusMessage = "Session expired, reconnecting...";
                    }

                    return (int)response.StatusCode switch
                    {
                        401 => "[Error] Player2: Not authenticated. Open the Player2 app and make sure you're logged in, or re-sign in via Settings.",
                        403 => "[Error] Player2: Access denied. Your session may have expired — sign in again via Settings.",
                        429 => "(!) Player2: Rate limit reached. Wait a moment before trying again.",
                        503 => "(!) Player2: Service unavailable. The Player2 servers may be down — try again shortly.",
                        404 => "[Error] Player2: Endpoint not found. The API may have changed — check for mod updates.",
                        _   => $"[Error] Player2 error ({(int)response.StatusCode}). Check the SMAPI log for details."
                    };
                }
            }
            catch (TaskCanceledException)
            {
                monitor.Log($"Player2 API timeout after {httpClient.Timeout.TotalSeconds}s", LogLevel.Warn);
                if (isLocal) localDetectionStatusMessage = "Response timeout, still connected";
                return "[Timeout] Player2 took too long to respond. Try again in a moment.";
            }
            catch (HttpRequestException ex)
            {
                monitor.Log($"Player2 connection error: {ex.Message}", LogLevel.Error);
                if (isLocal)
                {
                    localPlayer2Key = null;
                    localDetectionStatusMessage = "Connection lost";
                }
                bool isRefused = ex.Message.Contains("refused") || ex.Message.Contains("actively refused") ||
                                 ex.InnerException is System.Net.Sockets.SocketException;
                return isRefused
                    ? "[Error] Player2: Can't reach the app. Make sure the Player2 desktop app is open and running."
                    : $"[Error] Player2: Network error — {ex.Message}";
            }
            catch (Exception ex)
            {
                monitor.Log($"Player2 API exception: {ex.Message}", LogLevel.Error);
                if (isLocal)
                {
                    localPlayer2Key = null;
                    localDetectionStatusMessage = "Connection lost";
                }
                return "[Error] Player2: Unexpected error. Check the SMAPI log for details.";
            }
        }

        // ===== OLLAMA API =====
        private async Task<string> CallOllamaAPI(StardewValley.NPC npc, string prompt, string model = "default")
        {
            try
            {
                var endpoint = OllamaUrl;
                var requestData = new
                {
                    model,
                    messages = new[]
                    {
                        new { role = "system", content = GetNPCSystemPrompt(npc) },
                        new { role = "user", content = prompt }
                    },
                    stream = false
                };

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json")
                };

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return ParseOllamaFormatResponse(await response.Content.ReadAsStringAsync());

                var error = await response.Content.ReadAsStringAsync();
                monitor.Log($"Ollama API error: {response.StatusCode} - {error}", LogLevel.Warn);
                return (int)response.StatusCode switch
                {
                    400 => "[Error] Ollama: Bad request — your model may be invalid. Try a different model in Settings.",
                    429 => "(!) Ollama: Rate limit reached. Wait a moment before trying again.",
                    _   => $"[Error] Ollama error ({(int)response.StatusCode}). Check the SMAPI log for details."
                };
            }
            catch (TaskCanceledException)
            {
                monitor.Log($"Ollama API timeout after {httpClient.Timeout.TotalSeconds}s", LogLevel.Warn);
                return "[Timeout] Ollama took too long to respond. Try again in a moment.";
            }
            catch (Exception ex)
            {
                monitor.Log($"Ollama API exception: {ex.Message}", LogLevel.Error);
                return "[Error] Ollama: Connection error. Check the SMAPI log for details.";
            }
        }

        // ===== OPENROUTER API =====
        
        private async Task<string> CallOpenRouterAPI(StardewValley.NPC npc, string prompt, string apiKey, string model)
        {
            try
            {
                var endpoint = AIProvider.OpenRouter.GetEndpointUrl();
                var requestData = new
                {
                    model = model,
                    messages = new[]
                    {
                        new { role = "system", content = GetNPCSystemPrompt(npc) },
                        new { role = "user", content = prompt }
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                var extraHeaders = AIProvider.OpenRouter.GetExtraHeaders();
                if (extraHeaders != null)
                    foreach (var header in extraHeaders)
                        request.Headers.Add(header.Key, header.Value);

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return ParseOpenAIFormatResponse(await response.Content.ReadAsStringAsync());

                var error = await response.Content.ReadAsStringAsync();
                monitor.Log($"OpenRouter API error: {response.StatusCode} - {error}", LogLevel.Warn);
                return (int)response.StatusCode switch
                {
                    401 => "[Error] OpenRouter: Invalid API key. Check your key in Settings.",
                    429 => "(!) OpenRouter: Rate limit reached. Wait a moment before trying again.",
                    _   => $"[Error] OpenRouter error ({(int)response.StatusCode}). Check the SMAPI log for details."
                };
            }
            catch (TaskCanceledException)
            {
                monitor.Log($"OpenRouter API timeout after {httpClient.Timeout.TotalSeconds}s", LogLevel.Warn);
                return "[Timeout] OpenRouter took too long to respond. Try again in a moment.";
            }
            catch (Exception ex)
            {
                monitor.Log($"OpenRouter API exception: {ex.Message}", LogLevel.Error);
                return "[Error] OpenRouter: Connection error. Check the SMAPI log for details.";
            }
        }

        // ===== GOOGLE GEMINI API =====
        
        private async Task<string> CallGoogleAPI(StardewValley.NPC npc, string prompt, string apiKey, string model)
        {
            try
            {
                var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                var requestData = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = GetNPCSystemPrompt(npc) + "\n\n" + prompt } } }
                    },
                    generationConfig = new { temperature = 0.9, maxOutputTokens = 200 }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json")
                };

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return ParseGoogleResponse(await response.Content.ReadAsStringAsync());

                var error = await response.Content.ReadAsStringAsync();
                monitor.Log($"Google API error: {response.StatusCode} - {error}", LogLevel.Warn);
                return (int)response.StatusCode switch
                {
                    400 => "[Error] Google: Bad request — your model may be invalid. Try a different model in Settings.",
                    401 or 403 => "[Error] Google: Invalid API key. Check your key in Settings.",
                    429 => "(!) Google: Rate limit reached. Wait a moment before trying again.",
                    _   => $"[Error] Google error ({(int)response.StatusCode}). Check the SMAPI log for details."
                };
            }
            catch (TaskCanceledException)
            {
                monitor.Log($"Google API timeout after {httpClient.Timeout.TotalSeconds}s", LogLevel.Warn);
                return "[Timeout] Google took too long to respond. Try again in a moment.";
            }
            catch (Exception ex)
            {
                monitor.Log($"Google API exception: {ex.Message}", LogLevel.Error);
                return "[Error] Google: Connection error. Check the SMAPI log for details.";
            }
        }

        // ===== OPENAI API =====
        
        private async Task<string> CallOpenAIAPI(StardewValley.NPC npc, string prompt, string apiKey, string model)
        {
            try
            {
                var endpoint = AIProvider.OpenAI.GetEndpointUrl();
                var requestData = new
                {
                    model = model,
                    messages = new[]
                    {
                        new { role = "system", content = GetNPCSystemPrompt(npc) },
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 200,
                    temperature = 0.9
                };

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", $"Bearer {apiKey}");

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return ParseOpenAIFormatResponse(await response.Content.ReadAsStringAsync());

                var error = await response.Content.ReadAsStringAsync();
                monitor.Log($"OpenAI API error: {response.StatusCode} - {error}", LogLevel.Warn);
                return (int)response.StatusCode switch
                {
                    401 => "[Error] OpenAI: Invalid API key. Check your key in Settings.",
                    429 => "(!) OpenAI: Rate limit or quota reached. Check your OpenAI account.",
                    _   => $"[Error] OpenAI error ({(int)response.StatusCode}). Check the SMAPI log for details."
                };
            }
            catch (TaskCanceledException)
            {
                monitor.Log($"OpenAI API timeout after {httpClient.Timeout.TotalSeconds}s", LogLevel.Warn);
                return "[Timeout] OpenAI took too long to respond. Try again in a moment.";
            }
            catch (Exception ex)
            {
                monitor.Log($"OpenAI API exception: {ex.Message}", LogLevel.Error);
                return "[Error] OpenAI: Connection error. Check the SMAPI log for details.";
            }
        }

        // ===== PARSERS =====
        
        private string ParseOpenAIFormatResponse(string jsonResponse)
        {
            try
            {
                var jsonObj = JObject.Parse(jsonResponse);
                var choices = jsonObj["choices"];
                if (choices != null && choices.HasValues)
                {
                    var content = choices[0]["message"]?["content"]?.ToString();
                    if (!string.IsNullOrEmpty(content))
                        return CleanResponse(content);
                }

                var topLevelKeys = string.Join(", ", jsonObj.Properties().Select(p => p.Name));
                monitor.Log($"Unexpected response format. Top-level keys: [{topLevelKeys}]. Response: {jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length))}", LogLevel.Warn);
                return "[Error] Unexpected response format";
            }
            catch (Exception ex)
            {
                monitor.Log($"Failed to parse response: {ex.Message}", LogLevel.Error);
                return "[Error] Failed to parse AI response";
            }
        }

        private string ParseOllamaFormatResponse(string jsonResponse)
        {
            try
            {
                var jsonObj = JObject.Parse(jsonResponse);
                var message = jsonObj["message"];
                if (message != null && message.HasValues)
                {
                    var content = message["content"]?.ToString();
                    if (!string.IsNullOrEmpty(content))
                        return CleanResponse(content);
                }

                var topLevelKeys = string.Join(", ", jsonObj.Properties().Select(p => p.Name));
                monitor.Log($"Unexpected Ollama response format. Top-level keys: [{topLevelKeys}]. Response: {jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length))}", LogLevel.Warn);
                return "[Error] Unexpected response format";
            }
            catch (Exception ex)
            {
                monitor.Log($"Failed to parse Ollama response: {ex.Message}", LogLevel.Error);
                return "[Error] Failed to parse AI response";
            }
        }

        private string ParseGoogleResponse(string jsonResponse)
        {
            try
            {
                var jsonObj = JObject.Parse(jsonResponse);
                var text = jsonObj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                if (!string.IsNullOrEmpty(text))
                    return CleanResponse(text);
                
                var topLevelKeys = string.Join(", ", jsonObj.Properties().Select(p => p.Name));
                monitor.Log($"Unexpected Google response format. Top-level keys: [{topLevelKeys}]. Response: {jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length))}", LogLevel.Warn);
                return "[Error] Unexpected response format";
            }
            catch (Exception ex)
            {
                monitor.Log($"Failed to parse Google response: {ex.Message}", LogLevel.Error);
                return "[Error] Failed to parse AI response";
            }
        }

        private string CleanResponse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "...";
            text = text.Trim();
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length > 10)
            {
                var unwrapped = text.Substring(1, text.Length - 2);
                if (!unwrapped.Contains("\"") || unwrapped.Split(' ').Length < 5)
                    text = unwrapped;
            }
            return text;
        }

        // ===== SYSTEM PROMPT =====
        
        private string GetNPCSystemPrompt(StardewValley.NPC npc)
        {
            string personalitySection = "";
            
            if (ModEntry.ConfigManager != null)
            {
                var config = ModEntry.ConfigManager.GetNPCConfig(npc.Name);
                if (config.UseCustom && !string.IsNullOrEmpty(config.CustomPersonality))
                {
                    personalitySection = config.CustomPersonality;
                    monitor.Log($"Using custom personality for {npc.Name}", LogLevel.Debug);
                }
                else
                {
                    personalitySection = GetNativePersonalitySection(npc);
                }
            }
            else
            {
                personalitySection = GetNativePersonalitySection(npc);
            }
            
            string technicalInstructions = @"
CONVERSATION RULES:
- Keep responses to 1-2 sentences maximum
- Have natural, flowing conversations
- Listen actively and build on responses
- Ask genuine questions about their life
- Don't repeat yourself or get stuck on topics
- Balance talking about yourself with learning about the player
- Show genuine curiosity

IMPORTANT - AVOID OBSESSIVE BEHAVIOR:
- Do NOT constantly mention the player's equipment or appearance
- Do NOT comment on inventory unless directly relevant
- Focus on conversation content, not physical possessions
- Vary your topics naturally

ROLEPLAY & INTERACTIONS:
- Respond naturally to physical gestures and affection
- React based on friendship level and personality
- Show emotional responses appropriately
- Stay true to your character";
            
            string dynamicContext = GenerateDynamicContext(npc);
            
            string languageInstructions = "";
            if (ModEntry.LanguageManager != null)
                languageInstructions = "\n\nLANGUAGE: " + ModEntry.LanguageManager.GetSystemPromptAddition();

            //  FIX: Instrucciones globales definidas por el usuario en Settings
            string globalInstructions = "";
            if (ModEntry.ConfigManager != null)
            {
                var gi = ModEntry.ConfigManager.GetGlobalInstructions();
                if (!string.IsNullOrWhiteSpace(gi))
                    globalInstructions = $"\n\nADDITIONAL INSTRUCTIONS:\n{gi}";
            }
            
            return $@"{personalitySection}

{technicalInstructions}

{dynamicContext}
{languageInstructions}{globalInstructions}";
        }

        private string GetNativePersonalitySection(StardewValley.NPC npc)
        {
            string basePersonality = $"You are {npc.Name} from Stardew Valley.";
            
            if (ModEntry.PersonalityManager != null)
            {
                var personality = ModEntry.PersonalityManager.GetPersonality(npc.Name);
                if (personality != null)
                {
                    basePersonality = $@"You are {personality.Name} from Stardew Valley.

AGE & OCCUPATION:
{personality.Age}, {personality.Occupation}

CORE TRAITS:
{string.Join(", ", personality.CoreTraits)}

BACKGROUND:
{personality.BackgroundSummary}

SPEECH PATTERNS:
{string.Join("\n- ", personality.SpeechPatterns)}

INTERESTS:
{string.Join(", ", personality.Interests)}

DISLIKES:
{string.Join(", ", personality.Dislikes)}";

                    var locationName = StardewValley.Game1.currentLocation?.Name ?? "";
                    if (personality.LocationContext?.ContainsKey(locationName) == true)
                        basePersonality += $"\n\nLOCATION CONTEXT:\n{personality.LocationContext[locationName]}";
                    
                    string timeKey = StardewValley.Game1.timeOfDay < 1200 ? "Morning" : 
                                   StardewValley.Game1.timeOfDay < 1800 ? "Afternoon" : "Evening";
                    if (personality.TimeOfDayMoods?.ContainsKey(timeKey) == true)
                        basePersonality += $"\n\nCURRENT MOOD:\n{personality.TimeOfDayMoods[timeKey]}";
                }
            }
            
            return basePersonality;
        }

        //  FIX: Añade el nombre real del jugador al contexto para evitar [Player]
        private string GenerateDynamicContext(StardewValley.NPC npc)
        {
            var context = new System.Text.StringBuilder();

            // Obtener nombre del jugador — usa el apodo configurado si existe,
            // o el nombre real del juego como fallback
            string playerName = "the farmer";
            if (ModEntry.ConfigManager != null)
                playerName = ModEntry.ConfigManager.GetEffectivePlayerName();
            else
            {
                try { playerName = StardewValley.Game1.player?.Name ?? "the farmer"; }
                catch { }
            }

            context.AppendLine("\nCURRENT SITUATION:");
            context.AppendLine($"- The player's name is: {playerName} (always refer to them by this name, never use [Player])");
            context.AppendLine($"- You are at: {StardewValley.Game1.currentLocation?.DisplayName ?? "Unknown"}");
            context.AppendLine($"- Season: {StardewValley.Game1.currentSeason}");
            context.AppendLine($"- Time: {FormatGameTime(StardewValley.Game1.timeOfDay)}");
            
            if (StardewValley.Game1.isRaining)
                context.AppendLine("- Weather: It's raining");
            else if (StardewValley.Game1.isSnowing)
                context.AppendLine("- Weather: It's snowing");
            else
                context.AppendLine("- Weather: Clear day");
            
            int friendship = StardewValley.Game1.player.getFriendshipLevelForNPC(npc.Name);
            int hearts = friendship / 250;
            context.AppendLine($"- Friendship level: {hearts} hearts");
            
            if (hearts < 2) context.AppendLine("- Relationship: Just acquaintances, be polite but reserved");
            else if (hearts < 5) context.AppendLine("- Relationship: Friendly, warming up to them");
            else if (hearts < 8) context.AppendLine("- Relationship: Good friends, open and comfortable");
            else context.AppendLine("- Relationship: Very close friends, share personal thoughts");
            
            if (StardewValley.Game1.player.spouse == npc.Name)
                context.AppendLine("- Special: You are married to the player!");
            
            return context.ToString();
        }

        private string FormatGameTime(int time)
        {
            int hours = time / 100;
            int minutes = time % 100;
            string period = hours >= 12 ? "PM" : "AM";
            if (hours > 12) hours -= 12;
            if (hours == 0) hours = 12;
            return $"{hours}:{minutes:00} {period}";
        }

        // ===== FETCH MODELS =====
        
        public async Task<List<AIModelInfo>> FetchModelsForProvider(AIProvider provider, string apiKey)
        {
            try
            {
                return provider switch
                {
                    AIProvider.OpenRouter => await FetchOpenRouterModels(apiKey),
                    AIProvider.Ollama => await FetchOllamaModels(), // Ollama doesn't require API key for local model listing
                    AIProvider.Google => await FetchGoogleModels(apiKey),
                    AIProvider.OpenAI => await FetchOpenAIModels(apiKey),
                    AIProvider.Player2 => new List<AIModelInfo>(),
                    _ => new List<AIModelInfo>()
                };
            }
            catch (Exception ex)
            {
                monitor.Log($"Error fetching models for {provider}: {ex.Message}", LogLevel.Error);
                return new List<AIModelInfo>();
            }
        }

        private async Task<List<AIModelInfo>> FetchOpenRouterModels(string apiKey)
        {
            var models = new List<AIModelInfo>();
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, AIProvider.OpenRouter.GetListModelsUrl());
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var jsonObj = JObject.Parse(await response.Content.ReadAsStringAsync());
                    foreach (var model in jsonObj["data"] ?? new JArray())
                    {
                        var id = model["id"]?.ToString();
                        var name = model["name"]?.ToString() ?? id;
                        var contextLength = model["context_length"]?.ToObject<int?>();
                        if (!string.IsNullOrEmpty(id))
                            models.Add(new AIModelInfo(id, name) { ContextLength = contextLength });
                    }
                }
            }
            catch (Exception ex) { monitor.Log($"Error fetching OpenRouter models: {ex.Message}", LogLevel.Error); }
            return models;
        }

        private async Task<List<AIModelInfo>> FetchOllamaModels()
        {
            var models = new List<AIModelInfo>();
            try
            {
                var response = await httpClient.GetAsync(AIProvider.Ollama.GetListModelsUrl());
                if (response.IsSuccessStatusCode)
                {
                    var jsonObj = JArray.Parse(await response.Content.ReadAsStringAsync());
                    foreach (var model in jsonObj)
                    {
                        var name = model["name"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                            models.Add(new AIModelInfo(name, name));
                    }
                }
            }
            catch (Exception ex) { monitor.Log($"Error fetching Ollama models: {ex.Message}", LogLevel.Error); }
            return models;
        }

        private async Task<List<AIModelInfo>> FetchGoogleModels(string apiKey)
        {
            var models = new List<AIModelInfo>();
            try
            {
                var response = await httpClient.GetAsync($"{AIProvider.Google.GetListModelsUrl()}?key={apiKey}");
                if (response.IsSuccessStatusCode)
                {
                    var jsonObj = JObject.Parse(await response.Content.ReadAsStringAsync());
                    foreach (var model in jsonObj["models"] ?? new JArray())
                    {
                        var name = model["name"]?.ToString();
                        var displayName = model["displayName"]?.ToString();
                        if (!string.IsNullOrEmpty(name) && name.Contains("generateContent"))
                        {
                            var modelId = name.Replace("models/", "").Replace(":generateContent", "");
                            models.Add(new AIModelInfo(modelId, displayName ?? modelId));
                        }
                    }
                }
            }
            catch (Exception ex) { monitor.Log($"Error fetching Google models: {ex.Message}", LogLevel.Error); }
            return models;
        }

        private async Task<List<AIModelInfo>> FetchOpenAIModels(string apiKey)
        {
            var models = new List<AIModelInfo>();
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, AIProvider.OpenAI.GetListModelsUrl());
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var jsonObj = JObject.Parse(await response.Content.ReadAsStringAsync());
                    foreach (var model in jsonObj["data"] ?? new JArray())
                    {
                        var id = model["id"]?.ToString();
                        if (!string.IsNullOrEmpty(id) && (id.Contains("gpt") || id.Contains("o1")))
                            models.Add(new AIModelInfo(id, id));
                    }
                }
            }
            catch (Exception ex) { monitor.Log($"Error fetching OpenAI models: {ex.Message}", LogLevel.Error); }
            return models;
        }

        // ===== ESTADÍSTICAS =====
        
        public int GetTotalAPICallsThisSession() => totalAPICallsThisSession;
        public DateTime GetSessionStartTime() => sessionStartTime;
        public DateTime GetLastHealthPing() => lastHealthPing;

        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }
}