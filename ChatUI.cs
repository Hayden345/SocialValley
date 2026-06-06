using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SocialValley
{
    public class ChatUI : IClickableMenu
    {
        internal readonly IMonitor monitor; 
        private readonly UnifiedAIClient aiClient;  //  CAMBIADO: De Player2API a UnifiedAIClient
        private readonly NPC npc;
        
        // UI Elements
        private ClickableTextureComponent? chatBox;
        private ClickableTextureComponent? inputBox;
        private ClickableTextureComponent? sendButton;
        private ClickableTextureComponent? clearButton;
        private ClickableTextureComponent? closeButton;
        private ClickableTextureComponent? portraitBox;
        private ClickableTextureComponent? editModeButton;

        private int inputScrollOffset = 0;
        private bool inputHasFocus = true;
        
        // Input handling
        private KeyboardState previousKeyboardState;
        private KeyboardState currentKeyboardState;
        internal ChatTextReceiver? textReceiver;
        
        // Chat data
        private List<ChatMessage> chatHistory;
        private string currentInput = "";
        private int scrollOffset = 0;
        private bool isWaitingForResponse = false;

        // Smooth scrolling
        private float targetScrollOffset = 0f;
        private float currentScrollOffset = 0f;
        private bool isDraggingScrollbar = false;
        private ClickableTextureComponent? scrollBar;
        private ClickableTextureComponent? scrollThumb;
        private int scrollBarWidth = 16;
        private Vector2 dragStartPos;
        private float dragStartScroll;
        
        // Edit mode
        private bool isEditMode = false;
        private List<MessageActionButtons> messageActionButtons = new List<MessageActionButtons>();
        
        // Visual properties - Dynamic responsive sizing
        private int _lastViewportWidth = 0;
        private int _lastViewportHeight = 0;
        
        private static int chatWidth
        {
            get
            {
                return (int)Math.Max(600, (int)(Game1.uiViewport.Width * 0.5f));
            }
        }
        
        private static int chatHeight
        {
            get
            {
                return (int)Math.Max(600, (int)(Game1.uiViewport.Height * 0.9f));
            }
        }
        
        private static int inputHeight
        {
            get
            {
                return 120;
            }
        }
        
        private readonly int messageSpacing = 4;
        private readonly int maxChatMessages = 50;
        private readonly int portraitSize = 64;
        
        // Colors
        private readonly Color backgroundColor = new Color(36, 36, 36, 240);
        private readonly Color inputBackgroundColor = new Color(60, 60, 60, 255);
        private readonly Color playerMessageColor = new Color(150, 190, 255);
        private readonly Color npcMessageColor = new Color(180, 255, 180);
        private readonly Color textColor = Color.White;
        
        private bool isGeneratingWelcome = false;

        private ClickableTextureComponent? settingsButton;
        private ClickableTextureComponent? personalizeButton;

        
        //  CONSTRUCTOR ACTUALIZADO: Acepta UnifiedAIClient
        public ChatUI(IMonitor monitor, UnifiedAIClient aiClient, NPC npc)
            : base((Game1.uiViewport.Width - chatWidth) / 2, (Game1.uiViewport.Height - chatHeight) / 2, chatWidth, chatHeight)
        {
            this.monitor = monitor;
            this.aiClient = aiClient;
            this.npc = npc;
            this.chatHistory = new List<ChatMessage>();
            
            // Store current viewport size for resize detection
            _lastViewportWidth = Game1.uiViewport.Width;
            _lastViewportHeight = Game1.uiViewport.Height;

            monitor.Log($"ChatUI constructor called for {npc.Name}", LogLevel.Info);

            this.previousKeyboardState = Keyboard.GetState();
            this.currentKeyboardState = Keyboard.GetState();
            this.textReceiver = new ChatTextReceiver(this);

            monitor.Log($"Setting IsChatOpen to true (was: {ModEntry.IsChatOpen})", LogLevel.Info);
            ModEntry.IsChatOpen = true;

            InitializeComponents();
            LoadConversationHistory();
            AddWelcomeMessage();

            monitor.Log($"ChatUI construction completed for {npc.Name}", LogLevel.Info);
        }

        private void LoadConversationHistory()
        {
            if (ModEntry.ConversationManager == null) return;

            var history = ModEntry.ConversationManager.GetConversationHistory(npc);
            
            monitor.Log($"Loading {history.Count} previous messages for {npc.Name}", LogLevel.Debug);
            
            foreach (var entry in history)
            {
                chatHistory.Add(new ChatMessage(entry.Message, entry.IsFromPlayer));
            }
            
            ScrollToBottom();
        }

        private void InitializeComponents()
        {
            // Portrait area (top left)
            portraitBox = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + 16, yPositionOnScreen + 16, 
                portraitSize, portraitSize),
                null, Rectangle.Empty, 1f);

            // Chat area with fixed top margin
            int chatStartY = yPositionOnScreen + portraitSize + 45;
            int chatAreaHeight = chatHeight - portraitSize - inputHeight - 65;
            
            chatBox = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + 16, chatStartY, 
                chatWidth - 50, chatAreaHeight),
                null, Rectangle.Empty, 1f);

            // Input text box
            inputBox = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + 16, yPositionOnScreen + chatHeight - inputHeight - 8,
                chatWidth - 180, inputHeight),
                null, Rectangle.Empty, 1f);

            // Clear button
            clearButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + chatWidth - 160, yPositionOnScreen + chatHeight - inputHeight - 8,
                70, inputHeight),
                null, Rectangle.Empty, 1f);

            // Send button
            sendButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + chatWidth - 85, yPositionOnScreen + chatHeight - inputHeight - 8,
                70, inputHeight),
                null, Rectangle.Empty, 1f);

            // Close button
            closeButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + chatWidth - 36, yPositionOnScreen + 8,
                24, 24),
                null, Rectangle.Empty, 1f);

            // Edit mode button
            editModeButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + chatWidth - 120, yPositionOnScreen + 8, 80, 24),
                null, Rectangle.Empty, 1f);

            // Settings button
            settingsButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + chatWidth - 280, yPositionOnScreen + 8, 60, 24),
                null, Rectangle.Empty, 1f);

            // Personalize NPC button
            personalizeButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + chatWidth - 350, yPositionOnScreen + 8, 60, 24),
                null, Rectangle.Empty, 1f);  

            // Scrollbar
            scrollBar = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + chatWidth - scrollBarWidth - 16, 
                             chatStartY,
                             scrollBarWidth, 
                             chatAreaHeight),
                null, Rectangle.Empty, 1f);

            scrollThumb = new ClickableTextureComponent(
                new Rectangle(0, 0, scrollBarWidth - 4, 20),
                null, Rectangle.Empty, 1f);
                
            UpdateScrollThumb();
            UpdateMessageActionButtons();
        }

        private void AddWelcomeMessage()
        {
            if (chatHistory.Count == 0)
            {
                GenerateWelcomeMessage();
            }
        }

        private void UpdateScrollThumb()
        {
            if (scrollBar == null || scrollThumb == null || chatBox == null) return;

            int totalMessageHeight = GetTotalMessageHeight();
            int visibleHeight = chatBox.bounds.Height;
            int maxScroll = Math.Max(0, totalMessageHeight - visibleHeight);

            if (maxScroll <= 0)
            {
                scrollThumb.bounds = new Rectangle(0, 0, 0, 0);
                return;
            }

            float visibleRatio = (float)visibleHeight / totalMessageHeight;
            int thumbHeight = Math.Max(20, (int)(scrollBar.bounds.Height * visibleRatio));

            float scrollRatio = currentScrollOffset / maxScroll;
            int thumbY = scrollBar.bounds.Y + (int)((scrollBar.bounds.Height - thumbHeight) * scrollRatio);

            scrollThumb.bounds = new Rectangle(
                scrollBar.bounds.X + 2,
                thumbY,
                scrollBarWidth - 4,
                thumbHeight
            );
        }

        //  ACTUALIZADO: GenerateWelcomeMessage usa aiClient
        private async void GenerateWelcomeMessage()
        {
            if (isGeneratingWelcome) return;

            isGeneratingWelcome = true;
            AddMessage("...", false, true, false);

            try
            {
                string welcomePrompt = BuildWelcomePrompt();
                monitor.Log($"Generating welcome message for {npc.Name}", LogLevel.Debug);

                string response = await aiClient.GetResponse(npc, welcomePrompt);
                RemoveTypingIndicator();

                if (!string.IsNullOrEmpty(response) && !response.StartsWith("[Error]") && !response.StartsWith("(!)"))
                {
                    AddMessage(response, false, false, false);
                    monitor.Log($"Generated welcome: {response.Substring(0, Math.Min(50, response.Length))}...", LogLevel.Debug);
                }
                else
                {
                    string fallbackWelcome = GetFallbackWelcomeMessage();
                    AddMessage(fallbackWelcome, false, false, false);
                    monitor.Log($"Used fallback welcome for {npc.Name}", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"Error generating welcome for {npc.Name}: {ex.Message}", LogLevel.Error);
                RemoveTypingIndicator();
                string fallbackWelcome = GetFallbackWelcomeMessage();
                AddMessage(fallbackWelcome, false, false, false);
            }
            finally
            {
                isGeneratingWelcome = false;
            }
        }

        private string GetFallbackWelcomeMessage()
        {
            if (ModEntry.LanguageManager != null)
            {
                return ModEntry.LanguageManager.GetWelcomeMessage(npc.Name);
            }

            return npc.Name.ToLower() switch
            {
                "sebastian" => "Oh, hey...",
                "abigail" => "Hey! What's up?",
                "sam" => "Yo! How's it going?",
                "penny" => "Hello! How are you doing?",
                "alex" => "Hey there!",
                "harvey" => "Hello! I hope you're feeling well.",
                "elliott" => "Ah, greetings!",
                "shane" => "...What do you want?",
                "haley" => "Oh, it's you.",
                "leah" => "Hi there! Nice to see you.",
                "maru" => "Hello! I was just working on something.",
                "emily" => "Hi! Great energy today!",
                _ => "Hello! Nice to see you."
            };
        }

        private string BuildWelcomePrompt()
        {
            if (ModEntry.ConversationManager != null)
            {
                return ModEntry.ConversationManager.GetContextualWelcomePrompt(npc);
            }

            var currentTime = Game1.timeOfDay;
            var currentSeason = Game1.currentSeason;
            var currentLocation = Game1.currentLocation?.Name ?? "Unknown";
            var friendshipLevel = Game1.player.getFriendshipHeartLevelForNPC(npc.Name);

            string relationshipContext = friendshipLevel switch
            {
                0 => "This is the first time the player has approached you for a conversation",
                1 => "You've met the player once or twice before, but don't know them well yet",
                2 => "You're starting to recognize the player as a regular face around town",
                <= 4 => "You consider the player a friendly acquaintance",
                <= 6 => "The player has become a good friend",
                <= 8 => "You and the player are close friends",
                _ => "The player is one of your best friends in town"
            };

            return $@"The player has just approached you for the first time in this conversation. Give a natural greeting that shows your personality.

SITUATION CONTEXT:
- Time: {currentTime / 100}:{currentTime % 100:D2}
- Season: {currentSeason}  
- Location: {currentLocation}
- Your relationship: {relationshipContext} ({friendshipLevel} hearts)

GREETING INSTRUCTIONS:
- Give a natural, in-character greeting as {npc.Name}
- Reference the current time, location, or season if it makes sense
- Show your personality immediately through your greeting style
- Keep it brief (1-2 sentences max)
- React to the context naturally

Remember: This is a GREETING, not a full conversation starter. Just say hello in your unique way.";
        }

        private int GetTotalMessageHeight()
        {
            int totalHeight = 0;
            foreach (var msg in chatHistory)
            {
                int msgHeight = CalculateMessageHeight(msg.Text, chatBox?.bounds.Width ?? 600);
                totalHeight += msgHeight + messageSpacing;
            }
            return totalHeight;
        }

        // ===== EDIT MODE FUNCTIONALITY =====
        
        private void UpdateMessageActionButtons()
        {
            messageActionButtons.Clear();
            
            if (!isEditMode) return;
            
            for (int i = 0; i < chatHistory.Count; i++)
            {
                var message = chatHistory[i];
                if (message.IsTypingIndicator) continue;
                
                var actionButtons = new MessageActionButtons(i);
                messageActionButtons.Add(actionButtons);
            }
        }

        private void ToggleEditMode()
        {
            isEditMode = !isEditMode;
            UpdateMessageActionButtons();
            monitor.Log($"Edit mode: {(isEditMode ? "ON" : "OFF")}", LogLevel.Debug);
        }

        private void StartEditMessage(int messageIndex)
        {
            if (messageIndex < 0 || messageIndex >= chatHistory.Count) return;
            
            var message = chatHistory[messageIndex];
            if (message.IsTypingIndicator) return;
            
            monitor.Log($"Opening edit modal for message {messageIndex}", LogLevel.Debug);
            
            var editModal = new EditMessageModal(this, messageIndex, message.Text, message.IsFromPlayer, monitor);
            Game1.activeClickableMenu = editModal;
        }

        public void ApplyMessageEdit(int messageIndex, string newText)
        {
            if (messageIndex < 0 || messageIndex >= chatHistory.Count) return;
            
            var oldMessage = chatHistory[messageIndex];
            if (oldMessage.IsTypingIndicator) return;
            
            chatHistory[messageIndex] = new ChatMessage(newText, oldMessage.IsFromPlayer, false);
            
            UpdatePersistentHistory();
            
            monitor.Log($"Applied edit to message {messageIndex}: {newText.Substring(0, Math.Min(30, newText.Length))}...", LogLevel.Debug);
        }

        private void DeleteMessage(int messageIndex)
        {
            if (messageIndex < 0 || messageIndex >= chatHistory.Count) return;
            
            var messageToDelete = chatHistory[messageIndex];
            if (messageToDelete.IsTypingIndicator) return;
            
            if (messageToDelete.IsFromPlayer)
            {
                if (messageIndex + 1 < chatHistory.Count && 
                    !chatHistory[messageIndex + 1].IsFromPlayer && 
                    !chatHistory[messageIndex + 1].IsTypingIndicator)
                {
                    chatHistory.RemoveAt(messageIndex + 1);
                    chatHistory.RemoveAt(messageIndex);
                    monitor.Log($"Deleted player message and NPC response at index {messageIndex}", LogLevel.Debug);
                }
                else
                {
                    chatHistory.RemoveAt(messageIndex);
                    monitor.Log($"Deleted player message at index {messageIndex}", LogLevel.Debug);
                }
            }
            else
            {
                if (messageIndex > 0 && 
                    chatHistory[messageIndex - 1].IsFromPlayer && 
                    !chatHistory[messageIndex - 1].IsTypingIndicator)
                {
                    chatHistory.RemoveAt(messageIndex);
                    chatHistory.RemoveAt(messageIndex - 1);
                    monitor.Log($"Deleted NPC message and preceding player message at index {messageIndex}", LogLevel.Debug);
                }
                else
                {
                    chatHistory.RemoveAt(messageIndex);
                    monitor.Log($"Deleted NPC message at index {messageIndex}", LogLevel.Debug);
                }
            }
            
            UpdatePersistentHistory();
            UpdateMessageActionButtons();
        }

        //  ACTUALIZADO: RegenerateMessage usa aiClient
        private async void RegenerateMessage(int messageIndex)
        {
            if (messageIndex < 0 || messageIndex >= chatHistory.Count) return;
            
            var messageToRegenerate = chatHistory[messageIndex];
            if (messageToRegenerate.IsFromPlayer || messageToRegenerate.IsTypingIndicator) return;
            
            string playerMessage = "";
            for (int i = messageIndex - 1; i >= 0; i--)
            {
                if (chatHistory[i].IsFromPlayer && !chatHistory[i].IsTypingIndicator)
                {
                    playerMessage = chatHistory[i].Text;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(playerMessage))
            {
                monitor.Log("Cannot regenerate: no player message found", LogLevel.Warn);
                return;
            }
            
            monitor.Log($"Regenerating response for: {playerMessage.Substring(0, Math.Min(30, playerMessage.Length))}...", LogLevel.Debug);
            
            chatHistory[messageIndex] = new ChatMessage(" Regenerating...", false, true);
            
            try
            {
                string conversationContext = BuildContextUpToMessage(messageIndex - 1);
                string gameContext = "";
                
                if (ModEntry.ConversationManager != null)
                {
                    gameContext = ModEntry.ConversationManager.BuildGameContext(npc);
                }
                
                string prompt = $@"You are {npc.Name} from Stardew Valley. The player just said: ""{playerMessage}""

{gameContext}

{conversationContext}

Instructions for {npc.Name}:
- Stay completely in character as {npc.Name}
- Reference your personality, background, and relationships from the game
- Consider the current season, time, and your friendship level with the player
- Keep responses conversational and natural (1-3 sentences typically)
- You can reference past conversations naturally
- Show interest in the player's farming progress and life in Pelican Town
- React appropriately to the time of day and season";

                string response = await aiClient.GetResponse(npc, prompt);
                
                if (!string.IsNullOrEmpty(response) && !response.StartsWith("[Error]") && !response.StartsWith("(!)"))
                {
                    chatHistory[messageIndex] = new ChatMessage(response, false, false);
                    monitor.Log($"Regenerated response: {response.Substring(0, Math.Min(50, response.Length))}...", LogLevel.Debug);
                    UpdatePersistentHistory();
                }
                else
                {
                    chatHistory[messageIndex] = messageToRegenerate;
                    monitor.Log("Failed to regenerate message", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"Error regenerating message: {ex.Message}", LogLevel.Error);
                chatHistory[messageIndex] = messageToRegenerate;
            }
        }

        private string BuildContextUpToMessage(int maxMessageIndex)
        {
            if (ModEntry.ConversationManager == null) return "This is your conversation with the player.";
            
            var contextMessages = chatHistory
                .Take(maxMessageIndex + 1)
                .Where(m => !m.IsTypingIndicator)
                .ToList();
            
            if (!contextMessages.Any()) return "This is the start of your conversation with the player.";

            // Usar el nombre efectivo del jugador (nickname o nombre del juego)
            // para que el historial sea consistente con el system prompt
            string playerLabel = ModEntry.ConfigManager?.GetEffectivePlayerName() 
                                 ?? Game1.player.Name;
            
            var context = "Previous conversation:\n";
            foreach (var message in contextMessages)
            {
                string speaker = message.IsFromPlayer ? playerLabel : npc.Name;
                context += $"{speaker}: {message.Text}\n";
            }
            
            return context;
        }

        private void UpdatePersistentHistory()
        {
            if (ModEntry.ConversationManager == null) return;
            
            ModEntry.ConversationManager.ClearConversation(npc);
            
            foreach (var message in chatHistory.Where(m => !m.IsTypingIndicator))
            {
                ModEntry.ConversationManager.AddMessage(npc, message.Text, message.IsFromPlayer);
            }
            
            monitor.Log($"Updated persistent history for {npc.Name} - {chatHistory.Count(m => !m.IsTypingIndicator)} messages", LogLevel.Debug);
        }

        public override void update(GameTime time)
        {
            base.update(time);
            
            // Check if viewport size changed and recenter if needed
            if (Game1.uiViewport.Width != _lastViewportWidth || Game1.uiViewport.Height != _lastViewportHeight)
            {
                gameWindowSizeChanged(new Rectangle(xPositionOnScreen, yPositionOnScreen, chatWidth, chatHeight),
                                     new Rectangle(xPositionOnScreen, yPositionOnScreen, chatWidth, chatHeight));
            }

            previousKeyboardState = currentKeyboardState;
            currentKeyboardState = Keyboard.GetState();

            if (Game1.activeClickableMenu != this && ModEntry.IsChatOpen)
            {
                monitor.Log($"ChatUI lost menu control! ActiveMenu: {Game1.activeClickableMenu?.GetType().Name ?? "null"}", LogLevel.Warn);

                if (!(Game1.activeClickableMenu is EditMessageModal))
                {
                    monitor.Log("Lost control to non-modal, performing safe cleanup", LogLevel.Warn);
                    PerformSafeCleanup();
                    return;
                }
            }

            if (Game1.keyboardDispatcher.Subscriber != textReceiver &&
                Game1.activeClickableMenu == this &&
                !(Game1.activeClickableMenu is EditMessageModal))
            {
                Game1.keyboardDispatcher.Subscriber = textReceiver;
            }

            float scrollSpeed = 8f;
            float deltaTime = (float)time.ElapsedGameTime.TotalSeconds;

            if (Math.Abs(targetScrollOffset - currentScrollOffset) > 1f)
            {
                currentScrollOffset = MathHelper.Lerp(currentScrollOffset, targetScrollOffset, scrollSpeed * deltaTime);
            }
            else
            {
                currentScrollOffset = targetScrollOffset;
            }

            scrollOffset = (int)currentScrollOffset;
            UpdateScrollThumb();
        }

        private void PerformSafeCleanup()
        {
            try
            {
                monitor.Log("Performing safe cleanup to prevent crash", LogLevel.Info);

                if (Game1.keyboardDispatcher.Subscriber == textReceiver)
                {
                    Game1.keyboardDispatcher.Subscriber = null;
                    monitor.Log("Safely cleared keyboard subscriber", LogLevel.Debug);
                }

                if (ModEntry.IsChatOpen)
                {
                    ModEntry.IsChatOpen = false;
                    monitor.Log("Updated global chat state to false", LogLevel.Debug);
                }

                monitor.Log("Safe cleanup completed", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Error during safe cleanup: {ex.Message}", LogLevel.Error);
                ModEntry.IsChatOpen = false;
                if (Game1.keyboardDispatcher.Subscriber == textReceiver)
                {
                    Game1.keyboardDispatcher.Subscriber = null;
                }
            }
        }

        public void ForceCleanupState()
        {
            monitor.Log("Force cleanup called", LogLevel.Warn);

            if (Game1.keyboardDispatcher.Subscriber == textReceiver)
            {
                Game1.keyboardDispatcher.Subscriber = null;
            }

            ModEntry.IsChatOpen = false;
            monitor.Log("Forced IsChatOpen to false", LogLevel.Warn);
        }

        public override bool overrideSnappyMenuCursorMovementBan()
        {
            return true;
        }

        public void ReceiveTextInput(char inputChar)
        {
            if (char.IsControl(inputChar))
                return;
                
            if (currentInput.Length < 500)
            {
                currentInput += inputChar;
                inputScrollOffset = 0;
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            if (chatBox == null) return;
            
            int totalMessageHeight = GetTotalMessageHeight();
            int visibleHeight = chatBox.bounds.Height;
            int maxScroll = Math.Max(0, totalMessageHeight - visibleHeight);
            
            int scrollAmount = direction * 60;
            targetScrollOffset = Math.Max(0, Math.Min(maxScroll, targetScrollOffset - scrollAmount));
        }

        public void TrySendMessage()
        {
            if (!string.IsNullOrWhiteSpace(currentInput))
                SendMessage();
        }

        public void HandleBackspace()
        {
            if (currentInput.Length > 0)
            {
                currentInput = currentInput.Substring(0, currentInput.Length - 1);
                inputScrollOffset = 0;
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            if (settingsButton != null && settingsButton.containsPoint(x, y))
            {
                monitor.Log("Opening settings modal", LogLevel.Debug);
                Game1.activeClickableMenu = new SettingsModal(this, monitor);
                return;
            }

            if (personalizeButton != null && personalizeButton.containsPoint(x, y))
            {
                monitor.Log($"Opening personalization modal for {npc.Name}", LogLevel.Debug);
                Game1.activeClickableMenu = new PersonalizeNPCModal(this, npc, monitor);
                return;
            }

            if (editModeButton != null && editModeButton.containsPoint(x, y))
            {
                ToggleEditMode();
                return;
            }

            if (isEditMode)
            {
                foreach (var actionButtons in messageActionButtons)
                {
                    if (actionButtons.EditButton?.containsPoint(x, y) == true)
                    {
                        StartEditMessage(actionButtons.MessageIndex);
                        return;
                    }
                    
                    if (actionButtons.DeleteButton?.containsPoint(x, y) == true)
                    {
                        DeleteMessage(actionButtons.MessageIndex);
                        return;
                    }
                    
                    if (actionButtons.RegenerateButton?.containsPoint(x, y) == true)
                    {
                        RegenerateMessage(actionButtons.MessageIndex);
                        return;
                    }
                }
            }

            if (scrollBar != null && scrollBar.containsPoint(x, y))
            {
                if (scrollThumb != null && scrollThumb.containsPoint(x, y))
                {
                    isDraggingScrollbar = true;
                    dragStartPos = new Vector2(x, y);
                    dragStartScroll = targetScrollOffset;
                }
                else
                {
                    JumpToScrollPosition(y);
                }
                return;
            }

            if (sendButton != null && sendButton.containsPoint(x, y) && !string.IsNullOrWhiteSpace(currentInput))
            {
                SendMessage();
            }

            if (clearButton != null && clearButton.containsPoint(x, y))
            {
                currentInput = "";
                inputScrollOffset = 0;
            }

            if (closeButton != null && closeButton.containsPoint(x, y))
            {
                CleanupAndExit();
            }
        }

        private void JumpToScrollPosition(int clickY)
        {
            if (scrollBar == null || chatBox == null) return;

            int totalMessageHeight = GetTotalMessageHeight();
            int visibleHeight = chatBox.bounds.Height;
            int maxScroll = Math.Max(0, totalMessageHeight - visibleHeight);

            if (maxScroll <= 0) return;

            float relativeY = (float)(clickY - scrollBar.bounds.Y) / scrollBar.bounds.Height;
            relativeY = Math.Max(0, Math.Min(1, relativeY));

            targetScrollOffset = relativeY * maxScroll;
        }

        public override void leftClickHeld(int x, int y)
        {
            if (isDraggingScrollbar && scrollBar != null && chatBox != null)
            {
                int totalMessageHeight = GetTotalMessageHeight();
                int visibleHeight = chatBox.bounds.Height;
                int maxScroll = Math.Max(0, totalMessageHeight - visibleHeight);
                
                if (maxScroll <= 0) return;
                
                float deltaY = y - dragStartPos.Y;
                float scrollRange = scrollBar.bounds.Height;
                float scrollDelta = (deltaY / scrollRange) * maxScroll;
                
                targetScrollOffset = Math.Max(0, Math.Min(maxScroll, dragStartScroll + scrollDelta));
            }
        }

        public override void releaseLeftClick(int x, int y)
        {
            isDraggingScrollbar = false;
            base.releaseLeftClick(x, y);
        }

        //  ACTUALIZADO: SendMessage usa aiClient
        private async void SendMessage()
        {
            if (isWaitingForResponse || string.IsNullOrWhiteSpace(currentInput))
                return;

            string userMessage = currentInput.Trim();
            currentInput = "";
            inputScrollOffset = 0;

            // Resolve the player's display name for use in the prompt
            string playerName = ModEntry.ConfigManager?.GetEffectivePlayerName()
                             ?? Game1.player?.Name
                             ?? "the farmer";

            bool isFirstPlayerMessage = !chatHistory.Any(m => m.IsFromPlayer && !m.IsTypingIndicator);
            
            if (isFirstPlayerMessage && ModEntry.ConversationManager != null)
            {
                var welcomeMessage = chatHistory.FirstOrDefault(m => !m.IsFromPlayer && !m.IsTypingIndicator);
                if (welcomeMessage != null)
                {
                    ModEntry.ConversationManager.AddMessage(npc, welcomeMessage.Text, false);
                    monitor.Log($"Retroactively saved welcome message for {npc.Name}", LogLevel.Debug);
                }
            }

            AddMessage(userMessage, true, false, true);
            isWaitingForResponse = true;
            
            string typingIndicator = ModEntry.LanguageManager?.GetLocalizedUIText("typing_indicator") ?? "...";
            AddMessage(typingIndicator, false, true, false);

            try
            {
                string conversationContext = "";
                string gameContext = "";

                if (ModEntry.ConversationManager != null)
                {
                    conversationContext = ModEntry.ConversationManager.BuildConversationContext(npc);
                    gameContext = ModEntry.ConversationManager.BuildGameContext(npc);
                }
                else
                {
                    conversationContext = "This is the start of your conversation with the player.";
                    gameContext = "Current game information is not available.";
                }

                string contextualPrompt = $@"CURRENT SITUATION:
{gameContext}

CONVERSATION HISTORY:
{conversationContext}

PLAYER'S MESSAGE: ""{userMessage}""

RESPONSE GUIDANCE:
- The player just said the above message to you
- Respond naturally as {npc.Name} based on your personality and relationship
- Consider all the context: current situation, conversation history, and what they shared
- Have a genuine conversation - listen, respond thoughtfully, and show interest
- Build on their message rather than just defaulting to your usual topics

ROLEPLAY & INTERACTION HANDLING:
- If the player describes a physical action (hug, touch, gesture), respond naturally
- React with appropriate body language and emotional responses
- Show how the interaction affects you emotionally
- Match the intimacy level to your friendship with the player
- Be descriptive about your reactions and feelings when appropriate
- Don't ignore or dismiss physical or emotional roleplay - engage with it authentically

EMOTIONAL CONTEXT:
- Pay attention to the emotional tone of the player's message
- Respond with appropriate empathy, excitement, comfort, or support
- Show that you are emotionally present and engaged
- Express your own feelings and reactions naturally
- Build emotional connection through your responses

RESPONSE STYLE:
- Keep your response conversational and appropriately sized for the interaction
- For emotional or physical moments, you can be more descriptive and detailed
- Match the player's energy level and emotional investment
- Create immersive, meaningful interactions that feel real
- Ask questions when appropriate and share relevant experiences";

                monitor.Log($"Sending message to {npc.Name}: {userMessage.Substring(0, Math.Min(30, userMessage.Length))}...", LogLevel.Debug);

                string response = await aiClient.GetResponse(npc, contextualPrompt);

                RemoveTypingIndicator();

                if (!string.IsNullOrEmpty(response) && 
                    !response.StartsWith("[Error]") && 
                    !response.StartsWith("(!)") && 
                    !response.StartsWith("[Timeout]"))
                {
                    AddMessage(response, false, false, true);
                    monitor.Log($"Received response from {npc.Name}: {response.Substring(0, Math.Min(50, response.Length))}...", LogLevel.Debug);
                }
                else if (!string.IsNullOrEmpty(response))
                {
                    // Mostrar el error real del AI client en lugar del mensaje genérico.
                    AddMessage(response, false, false, false);
                    monitor.Log($"AI error for {npc.Name}: {response}", LogLevel.Warn);
                }
                else
                {
                    // Solo llega aquí si la respuesta es null o vacía (caso muy raro)
                    string fallback = ModEntry.LanguageManager?.GetLocalizedUIText("connection_error") ?? 
                                    "I'm having trouble connecting right now... maybe try again?";
                    AddMessage(fallback, false, false, false);
                    monitor.Log($"Empty response for {npc.Name}", LogLevel.Warn);
                }
            }
            catch (HttpRequestException httpEx)
            {
                monitor.Log($"HTTP error in chat with {npc.Name}: {httpEx.Message}", LogLevel.Error);
                RemoveTypingIndicator();
                
                string httpErrorMessage = ModEntry.LanguageManager?.GetLocalizedUIText("connection_error") ?? 
                                        "I'm having trouble connecting right now... maybe try again?";
                AddMessage(httpErrorMessage, false, false, false);
            }
            catch (TaskCanceledException timeoutEx)
            {
                monitor.Log($"Timeout error in chat with {npc.Name}: {timeoutEx.Message}", LogLevel.Error);
                RemoveTypingIndicator();
                
                string timeoutMessage = ModEntry.LanguageManager?.GetLocalizedUIText("timeout_error") ?? 
                                      "Sorry, that's taking too long to respond. Try again?";
                AddMessage(timeoutMessage, false, false, false);
            }
            catch (Exception ex)
            {
                monitor.Log($"Unexpected error in chat with {npc.Name}: {ex.Message}", LogLevel.Error);
                monitor.Log($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
                RemoveTypingIndicator();
                
                string genericErrorMessage = ModEntry.LanguageManager?.GetLocalizedUIText("error_response") ?? 
                                           "Sorry, I'm having trouble responding right now.";
                AddMessage(genericErrorMessage, false, false, false);
            }
            finally
            {
                isWaitingForResponse = false;
                
                if (textReceiver != null && Game1.keyboardDispatcher.Subscriber != textReceiver)
                {
                    Game1.keyboardDispatcher.Subscriber = textReceiver;
                }
            }
        }
        
        private void AddMessage(string text, bool isFromPlayer, bool isTypingIndicator, bool saveToHistory)
        {
            chatHistory.Add(new ChatMessage(text, isFromPlayer, isTypingIndicator));
            
            if (saveToHistory && !isTypingIndicator && ModEntry.ConversationManager != null)
            {
                ModEntry.ConversationManager.AddMessage(npc, text, isFromPlayer);
            }
            
            if (chatHistory.Count > maxChatMessages)
            {
                chatHistory.RemoveAt(0);
            }
            
            ScrollToBottom();
        }

        private void RemoveTypingIndicator()
        {
            chatHistory.RemoveAll(m => m.IsTypingIndicator);
        }

        private void ScrollToBottom()
        {
            if (chatBox == null) return;
            
            int totalMessageHeight = GetTotalMessageHeight();
            int visibleHeight = chatBox.bounds.Height;
            int maxScroll = Math.Max(0, totalMessageHeight - visibleHeight);
            
            targetScrollOffset = maxScroll;
        }

        private int CalculateMessageHeight(string text, int maxWidth)
        {
            var lines = WrapTextAdvanced(text, maxWidth);

            var lineHeight = Game1.smallFont.LineSpacing + 2;
            var paddingTop = 12;
            var paddingBottom = 12;
            var minimumHeight = 40;

            var calculatedHeight = (lines.Count * lineHeight) + paddingTop + paddingBottom;

            return Math.Max(minimumHeight, calculatedHeight);
        }

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
            
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), 
                xPositionOnScreen, yPositionOnScreen, chatWidth, chatHeight, Color.White);
            
            DrawNPCPortrait(b);
            DrawTitle(b);
            DrawChatMessages(b);
            DrawScrollBar(b);
            DrawInputArea(b);
            DrawEditModeButton(b);
            DrawSettingsButton(b);
            DrawPersonalizeButton(b);
            DrawCloseButton(b);
            
            if (isEditMode)
            {
                DrawMessageActionButtons(b);
            }
            
            drawMouse(b);
        }

        private void DrawNPCPortrait(SpriteBatch b)
        {
            if (portraitBox == null) return;
            
            try
            {
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    portraitBox.bounds.X - 4, portraitBox.bounds.Y - 4, 
                    portraitBox.bounds.Width + 8, portraitBox.bounds.Height + 8, Color.White);

                var portraitTexture = npc.Portrait;
                if (portraitTexture != null)
                {
                    var sourceRect = new Rectangle(0, 0, 64, 64);
                    b.Draw(portraitTexture, portraitBox.bounds, sourceRect, Color.White);
                }
                else
                {
                    Utility.drawTextWithShadow(b, npc.Name.Substring(0, 1), Game1.dialogueFont,
                        new Vector2(portraitBox.bounds.X + 20, portraitBox.bounds.Y + 15), Color.White);
                }
            }
            catch
            {
                Utility.drawTextWithShadow(b, npc.Name.Substring(0, 1), Game1.dialogueFont,
                    new Vector2(portraitBox.bounds.X + 20, portraitBox.bounds.Y + 15), Color.White);
            }
        }

        private void DrawTitle(SpriteBatch b)
        {
            if (portraitBox == null) return;

            string title;
            if (ModEntry.LanguageManager != null)
            {
                string template = ModEntry.LanguageManager.GetLocalizedUIText("chat_title");
                title = string.Format(template, npc.displayName);
            }
            else
            {
                title = $"Chatting with {npc.displayName}";
            }

            SpriteFont fontToUse;

            if (FontManager.ContainsCJKCharacters(title))
            {
                fontToUse = FontManager.GetFontForText(title);
            }
            else
            {
                fontToUse = Game1.dialogueFont;
            }

            Vector2 titlePos = new Vector2(portraitBox.bounds.Right + 16, portraitBox.bounds.Y + 16);

            try
            {
                b.DrawString(fontToUse, title, titlePos, textColor);
            }
            catch
            {
                Utility.drawTextWithShadow(b, title, Game1.dialogueFont, titlePos, textColor);
            }

            var lineStart = new Vector2(xPositionOnScreen + 16, portraitBox.bounds.Bottom + 8);
            var lineEnd = new Vector2(xPositionOnScreen + chatWidth - 32, portraitBox.bounds.Bottom + 8);

            var lineRect = new Rectangle((int)lineStart.X, (int)lineStart.Y, (int)(lineEnd.X - lineStart.X), 2);
            b.Draw(Game1.fadeToBlackRect, lineRect, new Color(100, 100, 100, 150));
        }

        private void DrawChatMessages(SpriteBatch b)
        {
            if (chatBox == null) return;

            var originalViewport = b.GraphicsDevice.Viewport;

            var chatViewport = new Viewport
            {
                X = Math.Max(0, chatBox.bounds.X),
                Y = Math.Max(0, chatBox.bounds.Y),
                Width = Math.Min(chatBox.bounds.Width, Game1.graphics.GraphicsDevice.Viewport.Width - chatBox.bounds.X),
                Height = Math.Min(chatBox.bounds.Height, Game1.graphics.GraphicsDevice.Viewport.Height - chatBox.bounds.Y),
                MinDepth = 0,
                MaxDepth = 1
            };

            b.GraphicsDevice.Viewport = chatViewport;

            var chatBackground = new Rectangle(
                chatBox.bounds.X - originalViewport.X,
                chatBox.bounds.Y - originalViewport.Y,
                chatBox.bounds.Width,
                chatBox.bounds.Height
            );
            b.Draw(Game1.fadeToBlackRect, chatBackground, new Color(20, 20, 20, 100));

            int yOffset = (chatBox.bounds.Y - originalViewport.Y) - (int)currentScrollOffset;

            foreach (var message in chatHistory)
            {
                int messageHeight = CalculateMessageHeight(message.Text, chatBox.bounds.Width);

                var messageTop = yOffset;
                var messageBottom = yOffset + messageHeight;
                var viewportTop = chatBox.bounds.Y - originalViewport.Y;
                var viewportBottom = viewportTop + chatBox.bounds.Height;

                if (messageBottom > viewportTop - 50 && messageTop < viewportBottom + 50)
                {
                    int adjustedX = chatBox.bounds.X - originalViewport.X;
                    DrawMessage(b, message, adjustedX, yOffset, chatBox.bounds.Width);
                }

                yOffset += messageHeight + messageSpacing;

                if (yOffset > viewportBottom + 200)
                    break;
            }

            b.GraphicsDevice.Viewport = originalViewport;
        }

        //  FIX: Word wrap correcto para japonés/chino/coreano.
        // El texto CJK no usa espacios entre palabras, así que el wrap
        // por espacios nunca hace saltos. Se añade wrap carácter a carácter.
        private List<string> WrapTextAdvanced(string text, int maxWidth, SpriteFont? font = null)
        {
            if (font == null) font = Game1.smallFont;
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add("");
                return lines;
            }

            var effectiveWidth = maxWidth - 40;
            var paragraphs = text.Split('\n');

            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrEmpty(paragraph))
                {
                    lines.Add("");
                    continue;
                }

                // ── CJK: wrap carácter por carácter ──
                if (FontManager.ContainsCJKCharacters(paragraph))
                {
                    var currentCJKLine = "";
                    foreach (char ch in paragraph)
                    {
                        var testLine = currentCJKLine + ch;
                        if (font.MeasureString(testLine).X > effectiveWidth && currentCJKLine.Length > 0)
                        {
                            lines.Add(currentCJKLine);
                            currentCJKLine = ch.ToString();
                        }
                        else
                        {
                            currentCJKLine = testLine;
                        }
                    }
                    if (!string.IsNullOrEmpty(currentCJKLine))
                        lines.Add(currentCJKLine);
                    continue;
                }

                // ── Texto occidental: wrap por palabras (comportamiento original) ──
                var words = paragraph.Split(' ');
                var currentLine = "";

                foreach (var word in words)
                {
                    var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                    var testWidth = Game1.smallFont.MeasureString(testLine).X;

                    if (testWidth > effectiveWidth && !string.IsNullOrEmpty(currentLine))
                    {
                        lines.Add(currentLine);
                        currentLine = word;

                        while (Game1.smallFont.MeasureString(currentLine).X > effectiveWidth && currentLine.Length > 1)
                        {
                            int breakPoint = currentLine.Length - 1;

                            for (int i = currentLine.Length - 1; i > 0; i--)
                            {
                                if (currentLine[i] == '-' || currentLine[i] == '.' || currentLine[i] == ',')
                                {
                                    breakPoint = i + 1;
                                    break;
                                }
                            }

                            if (breakPoint == currentLine.Length - 1)
                            {
                                while (breakPoint > 0 && Game1.smallFont.MeasureString(currentLine.Substring(0, breakPoint)).X > effectiveWidth)
                                {
                                    breakPoint--;
                                }
                            }

                            if (breakPoint > 0)
                            {
                                lines.Add(currentLine.Substring(0, breakPoint));
                                currentLine = currentLine.Substring(breakPoint);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        currentLine = testLine;
                    }
                }

                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                }
            }

            return lines.Count > 0 ? lines : new List<string> { text };
        }

        private List<string> WrapTextForInput(string text, int maxWidth, SpriteFont? font = null)
        {
            if (font == null) font = Game1.smallFont;
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add("");
                return lines;
            }

            var paragraphs = text.Split('\n');

            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrEmpty(paragraph))
                {
                    lines.Add("");
                    continue;
                }

                if (FontManager.ContainsCJKCharacters(paragraph))
                {
                    var currentCJKLine = "";
                    foreach (char ch in paragraph)
                    {
                        var testLine = currentCJKLine + ch;
                        if (font.MeasureString(testLine).X > maxWidth && currentCJKLine.Length > 0)
                        {
                            lines.Add(currentCJKLine);
                            currentCJKLine = ch.ToString();
                        }
                        else
                        {
                            currentCJKLine = testLine;
                        }
                    }
                    if (!string.IsNullOrEmpty(currentCJKLine))
                        lines.Add(currentCJKLine);
                    continue;
                }

                var words = paragraph.Split(' ');
                var currentLine = "";

                foreach (var word in words)
                {
                    var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                    var testWidth = font.MeasureString(testLine).X;

                    if (testWidth > maxWidth && !string.IsNullOrEmpty(currentLine))
                    {
                        lines.Add(currentLine);
                        currentLine = word;

                        while (font.MeasureString(currentLine).X > maxWidth && currentLine.Length > 1)
                        {
                            int breakPoint = currentLine.Length - 1;

                            for (int i = currentLine.Length - 1; i > 0; i--)
                            {
                                if (currentLine[i] == '-' || currentLine[i] == '.' || currentLine[i] == ',')
                                {
                                    breakPoint = i + 1;
                                    break;
                                }
                            }

                            if (breakPoint == currentLine.Length - 1)
                            {
                                while (breakPoint > 0 && font.MeasureString(currentLine.Substring(0, breakPoint)).X > maxWidth)
                                {
                                    breakPoint--;
                                }
                            }

                            if (breakPoint > 0)
                            {
                                lines.Add(currentLine.Substring(0, breakPoint));
                                currentLine = currentLine.Substring(breakPoint);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        currentLine = testLine;
                    }
                }

                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                }
            }

            return lines.Count > 0 ? lines : new List<string> { text };
        }

        private void DrawSettingsButton(SpriteBatch b)
        {
            if (settingsButton == null) return;

            settingsButton.bounds = new Rectangle(
                xPositionOnScreen + chatWidth - 280, yPositionOnScreen + 8, 60, 24);

            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                settingsButton.bounds.X, settingsButton.bounds.Y,
                settingsButton.bounds.Width, settingsButton.bounds.Height,
                new Color(100, 100, 200) * 0.8f);

            string text = "Config";
            Vector2 textSize = Game1.smallFont.MeasureString(text);
            Utility.drawTextWithShadow(b, text, Game1.smallFont,
                new Vector2(settingsButton.bounds.X + (settingsButton.bounds.Width - textSize.X) / 2,
                        settingsButton.bounds.Y + (settingsButton.bounds.Height - textSize.Y) / 2),
                Color.White);
        }

        private void DrawPersonalizeButton(SpriteBatch b)
        {
            if (personalizeButton == null) return;

            personalizeButton.bounds = new Rectangle(
                xPositionOnScreen + chatWidth - 350, yPositionOnScreen + 8, 60, 24);

            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                personalizeButton.bounds.X, personalizeButton.bounds.Y,
                personalizeButton.bounds.Width, personalizeButton.bounds.Height,
                new Color(200, 100, 200) * 0.8f);

            string text = "Prompt";
            Vector2 textSize = Game1.smallFont.MeasureString(text);
            Utility.drawTextWithShadow(b, text, Game1.smallFont,
                new Vector2(personalizeButton.bounds.X + (personalizeButton.bounds.Width - textSize.X) / 2,
                        personalizeButton.bounds.Y + (personalizeButton.bounds.Height - textSize.Y) / 2),
                Color.White);
        }

        private void DrawMessage(SpriteBatch b, ChatMessage message, int x, int y, int maxWidth)
        {
            Color bgColor = message.IsFromPlayer ? new Color(30, 50, 100, 220) : new Color(40, 80, 40, 220);
            Color textColor = Color.White;

            if (message.IsTypingIndicator)
            {
                textColor = Color.LightGray;
                bgColor = new Color(60, 60, 60, 200);
            }

            SpriteFont fontToUse = FontManager.GetFontForText(message.Text);
            bool needsCJKHandling = FontManager.ContainsCJKCharacters(message.Text);
            
            var lines = WrapTextAdvanced(message.Text, maxWidth - 80, fontToUse);
            var lineHeight = fontToUse.LineSpacing + 2;
            var paddingX = 16;
            var paddingY = 12;

            var actualMaxLineWidth = 0;
            foreach (var line in lines)
            {
                var lineWidth = (int)Game1.smallFont.MeasureString(line).X;
                if (lineWidth > actualMaxLineWidth)
                {
                    actualMaxLineWidth = lineWidth;
                }
            }

            var messageWidth = Math.Min(maxWidth - 80, actualMaxLineWidth + (paddingX * 2));
            var messageHeight = (lines.Count * lineHeight) + (paddingY * 2);

            var msgX = message.IsFromPlayer ?
                x + maxWidth - messageWidth - 40 :
                x + 40;

            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                msgX, y, messageWidth, messageHeight, bgColor);

            for (int i = 0; i < lines.Count; i++)
            {
                var lineY = y + paddingY + (i * lineHeight);
                var lineX = msgX + paddingX;

                Utility.drawTextWithShadow(b, lines[i], Game1.smallFont,
                    new Vector2(lineX, lineY), textColor, 1f, -1f, 2, 2);
            }
        }

        private void DrawScrollBar(SpriteBatch b)
        {
            if (scrollBar == null || scrollThumb == null) return;
            
            int totalMessageHeight = GetTotalMessageHeight();
            int visibleHeight = chatBox?.bounds.Height ?? 0;
            
            if (totalMessageHeight <= visibleHeight) return;
            
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                scrollBar.bounds.X - 2, scrollBar.bounds.Y - 2, 
                scrollBar.bounds.Width + 4, scrollBar.bounds.Height + 4, 
                new Color(40, 40, 40, 200));
            
            if (scrollThumb.bounds.Width > 0 && scrollThumb.bounds.Height > 0)
            {
                Color thumbColor = isDraggingScrollbar ? new Color(150, 150, 150, 255) : new Color(100, 100, 100, 200);
                
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                    scrollThumb.bounds.X, scrollThumb.bounds.Y,
                    scrollThumb.bounds.Width, scrollThumb.bounds.Height,
                    thumbColor);
            }
        }

        private SpriteFont GetAppropriateFont()
        {
            var currentLanguage = LocalizedContentManager.CurrentLanguageCode;
            
            switch (currentLanguage)
            {
                case LocalizedContentManager.LanguageCode.zh:
                case LocalizedContentManager.LanguageCode.ja:
                case LocalizedContentManager.LanguageCode.ko:
                    return Game1.smallFont;
                default:
                    return Game1.smallFont;
            }
        }

        private void DrawInputArea(SpriteBatch b)
        {
            if (inputBox == null || sendButton == null || clearButton == null) return;

            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                inputBox.bounds.X, inputBox.bounds.Y, inputBox.bounds.Width, inputBox.bounds.Height,
                inputBackgroundColor);

            SpriteFont fontToUse = FontManager.GetFontForText(currentInput);
            var inputWidth = inputBox.bounds.Width - 16;
            var wrappedLines = WrapTextForInput(currentInput, inputWidth, fontToUse);
            var lineHeight = fontToUse.LineSpacing + 2;
            const int maxLines = 3;
            var firstVisibleLine = Math.Max(0, wrappedLines.Count - maxLines);

            for (int i = firstVisibleLine; i < wrappedLines.Count; i++)
            {
                var line = wrappedLines[i];
                if (i == wrappedLines.Count - 1 && Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1000 < 500)
                {
                    line += "|";
                }

                try
                {
                    b.DrawString(fontToUse, line,
                        new Vector2(inputBox.bounds.X + 8, inputBox.bounds.Y + 8 + (i - firstVisibleLine) * lineHeight),
                        textColor);
                }
                catch
                {
                    Utility.drawTextWithShadow(b, line, Game1.smallFont,
                        new Vector2(inputBox.bounds.X + 8, inputBox.bounds.Y + 8 + (i - firstVisibleLine) * lineHeight),
                        textColor);
                }
            }

            string clearText = ModEntry.LanguageManager?.GetLocalizedUIText("clear_button") ?? "Clear";
            Color clearColor = string.IsNullOrWhiteSpace(currentInput) ? Color.Gray : Color.White;

            SpriteFont clearFont = FontManager.GetFontForText(clearText);

            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                clearButton.bounds.X, clearButton.bounds.Y, clearButton.bounds.Width, clearButton.bounds.Height,
                clearColor * 0.6f);

            Vector2 clearTextSize = clearFont.MeasureString(clearText);

            try
            {
                b.DrawString(clearFont, clearText,
                    new Vector2(clearButton.bounds.X + (clearButton.bounds.Width - clearTextSize.X) / 2,
                            clearButton.bounds.Y + (clearButton.bounds.Height - clearTextSize.Y) / 2),
                    clearColor);
            }
            catch
            {
                Utility.drawTextWithShadow(b, clearText, Game1.smallFont,
                    new Vector2(clearButton.bounds.X + (clearButton.bounds.Width - clearTextSize.X) / 2,
                            clearButton.bounds.Y + (clearButton.bounds.Height - clearTextSize.Y) / 2),
                    clearColor);
            }

            string sendText = isWaitingForResponse
                ? (ModEntry.LanguageManager?.GetLocalizedUIText("loading") ?? "...")
                : (ModEntry.LanguageManager?.GetLocalizedUIText("send_button") ?? "Send");

            Color sendColor = (isWaitingForResponse || string.IsNullOrWhiteSpace(currentInput))
                ? Color.Gray : Color.White;

            SpriteFont sendFont = FontManager.GetFontForText(sendText);

            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                sendButton.bounds.X, sendButton.bounds.Y, sendButton.bounds.Width, sendButton.bounds.Height,
                sendColor * 0.8f);

            Vector2 sendTextSize = sendFont.MeasureString(sendText);

            try
            {
                b.DrawString(sendFont, sendText,
                    new Vector2(sendButton.bounds.X + (sendButton.bounds.Width - sendTextSize.X) / 2,
                            sendButton.bounds.Y + (sendButton.bounds.Height - sendTextSize.Y) / 2),
                    sendColor);
            }
            catch
            {
                Utility.drawTextWithShadow(b, sendText, Game1.smallFont,
                    new Vector2(sendButton.bounds.X + (sendButton.bounds.Width - sendTextSize.X) / 2,
                            sendButton.bounds.Y + (sendButton.bounds.Height - sendTextSize.Y) / 2),
                    sendColor);
            }
        }

        private void DrawEditModeButton(SpriteBatch b)
        {
            if (editModeButton == null) return;
            
            string editModeText = isEditMode ? "Exit Edit" : "Edit Mode";
            Color buttonColor = isEditMode ? new Color(100, 200, 100) : new Color(200, 200, 100);
            
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                editModeButton.bounds.X, editModeButton.bounds.Y,
                editModeButton.bounds.Width, editModeButton.bounds.Height,
                buttonColor * 0.8f);
            
            Vector2 textSize = Game1.smallFont.MeasureString(editModeText);
            Utility.drawTextWithShadow(b, editModeText, Game1.smallFont,
                new Vector2(editModeButton.bounds.X + (editModeButton.bounds.Width - textSize.X) / 2,
                        editModeButton.bounds.Y + (editModeButton.bounds.Height - textSize.Y) / 2),
                Color.White);
        }

        private void DrawMessageActionButtons(SpriteBatch b)
        {
            if (chatBox == null) return;

            int yOffset = chatBox.bounds.Y - (int)currentScrollOffset;

            for (int i = 0; i < chatHistory.Count; i++)
            {
                var message = chatHistory[i];
                if (message.IsTypingIndicator) continue;

                int messageHeight = CalculateMessageHeight(message.Text, chatBox.bounds.Width - 20);

                if (yOffset + messageHeight > chatBox.bounds.Y && yOffset < chatBox.bounds.Y + chatBox.bounds.Height)
                {
                    var actionButtons = messageActionButtons.FirstOrDefault(ab => ab.MessageIndex == i);
                    if (actionButtons != null)
                    {
                        int buttonX = chatBox.bounds.Right - 175;
                        int buttonY = yOffset + 4;
                        int btnH = 22;

                        actionButtons.EditButton = new ClickableTextureComponent(
                            new Rectangle(buttonX, buttonY, 50, btnH), null, Rectangle.Empty, 1f);
                        DrawSmallButton(b, actionButtons.EditButton.bounds, "Edit", Color.DarkGoldenrod);

                        actionButtons.DeleteButton = new ClickableTextureComponent(
                            new Rectangle(buttonX + 55, buttonY, 58, btnH), null, Rectangle.Empty, 1f);
                        DrawSmallButton(b, actionButtons.DeleteButton.bounds, "Delete", Color.DarkRed);

                        if (!message.IsFromPlayer)
                        {
                            actionButtons.RegenerateButton = new ClickableTextureComponent(
                                new Rectangle(buttonX + 118, buttonY, 50, btnH), null, Rectangle.Empty, 1f);
                            DrawSmallButton(b, actionButtons.RegenerateButton.bounds, "Redo", new Color(0, 120, 0));
                        }
                    }
                }

                yOffset += messageHeight + messageSpacing;
            }
        }

        private void DrawSmallButton(SpriteBatch b, Rectangle bounds, string icon, Color color)
        {
            b.Draw(Game1.fadeToBlackRect, bounds, color * 0.7f);
            
            b.Draw(Game1.fadeToBlackRect, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), Color.White);
            b.Draw(Game1.fadeToBlackRect, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), Color.White);
            b.Draw(Game1.fadeToBlackRect, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), Color.White);
            b.Draw(Game1.fadeToBlackRect, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), Color.White);
            
            Vector2 iconSize = Game1.smallFont.MeasureString(icon);
            Vector2 iconPos = new Vector2(
                bounds.X + (bounds.Width - iconSize.X) / 2,
                bounds.Y + (bounds.Height - iconSize.Y) / 2
            );
            
            Utility.drawTextWithShadow(b, icon, Game1.smallFont, iconPos, Color.White, 1f, -1f, 1, 1);
        }
        
        private void DrawCloseButton(SpriteBatch b)
        {
            if (closeButton == null) return;
            
            Utility.drawTextWithShadow(b, "X", Game1.dialogueFont,
                new Vector2(closeButton.bounds.X + 2, closeButton.bounds.Y - 2), Color.White);
        }

        public void CleanupAndExit()
        {
            monitor.Log($"CleanupAndExit called for {npc.Name}", LogLevel.Info);
            monitor.Log($"Current state - ActiveMenu: {Game1.activeClickableMenu?.GetType().Name ?? "null"}, IsChatOpen: {ModEntry.IsChatOpen}", LogLevel.Info);

            try
            {
                if (Game1.keyboardDispatcher.Subscriber == textReceiver)
                {
                    Game1.keyboardDispatcher.Subscriber = null;
                    monitor.Log("Keyboard subscriber cleared", LogLevel.Debug);
                }
                else
                {
                    monitor.Log($"Keyboard subscriber was not ours: {Game1.keyboardDispatcher.Subscriber?.GetType().Name ?? "null"}", LogLevel.Debug);
                }

                monitor.Log($"Setting IsChatOpen to false (was: {ModEntry.IsChatOpen})", LogLevel.Info);
                ModEntry.IsChatOpen = false;

                monitor.Log($"Chat cleanup completed for {npc.Name}", LogLevel.Info);
                exitThisMenu();

                monitor.Log("exitThisMenu completed successfully", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Error during CleanupAndExit: {ex.Message}", LogLevel.Error);
                monitor.Log($"Stack trace: {ex.StackTrace}", LogLevel.Debug);

                ModEntry.IsChatOpen = false;
                Game1.activeClickableMenu = null;
                monitor.Log("Performed emergency cleanup", LogLevel.Warn);
            }
        }

        public void RestoreFromModal()
        {
            monitor.Log("=== RESTORE FROM MODAL CALLED ===", LogLevel.Info);
            monitor.Log($"Current menu before restore: {Game1.activeClickableMenu?.GetType().Name ?? "null"}", LogLevel.Info);
            monitor.Log($"IsChatOpen before restore: {ModEntry.IsChatOpen}", LogLevel.Info);

            try
            {
                if (textReceiver == null)
                {
                    monitor.Log("textReceiver is null, recreating", LogLevel.Warn);
                    textReceiver = new ChatTextReceiver(this);
                }

                if (Game1.activeClickableMenu == null || Game1.activeClickableMenu is EditMessageModal)
                {
                    Game1.activeClickableMenu = this;
                    monitor.Log("ChatUI restored as active menu", LogLevel.Info);
                }
                else
                {
                    monitor.Log($"Cannot restore - conflicting menu: {Game1.activeClickableMenu.GetType().Name}", LogLevel.Warn);
                    return;
                }

                if (Game1.keyboardDispatcher.Subscriber == null || Game1.keyboardDispatcher.Subscriber == textReceiver)
                {
                    Game1.keyboardDispatcher.Subscriber = textReceiver;
                    monitor.Log("ChatUI keyboard subscriber restored", LogLevel.Info);
                }
                else
                {
                    monitor.Log($"Cannot restore keyboard - conflict: {Game1.keyboardDispatcher.Subscriber.GetType().Name}", LogLevel.Warn);
                }

                if (!ModEntry.IsChatOpen)
                {
                    monitor.Log("Updating IsChatOpen to true", LogLevel.Info);
                    ModEntry.IsChatOpen = true;
                }

                monitor.Log($"Final state - ActiveMenu: {Game1.activeClickableMenu?.GetType().Name ?? "null"}", LogLevel.Info);
                monitor.Log($"Final state - IsChatOpen: {ModEntry.IsChatOpen}", LogLevel.Info);
                monitor.Log("=== ChatUI restoration completed successfully ===", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"ERROR in RestoreFromModal: {ex.Message}", LogLevel.Error);
                monitor.Log($"Stack trace: {ex.StackTrace}", LogLevel.Error);

                monitor.Log("Restoration failed, but not performing cleanup to avoid crash", LogLevel.Error);
            }
        }

        public override void emergencyShutDown()
        {
            monitor.Log($"Emergency shutdown called for {npc.Name}", LogLevel.Warn);

            try
            {
                if (Game1.keyboardDispatcher.Subscriber == textReceiver)
                {
                    Game1.keyboardDispatcher.Subscriber = null;
                }

                ModEntry.IsChatOpen = false;

                base.emergencyShutDown();
            }
            catch (Exception ex)
            {
                monitor.Log($"Error in emergency shutdown: {ex.Message}", LogLevel.Error);
                ModEntry.IsChatOpen = false;
            }
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            _lastViewportWidth = Game1.uiViewport.Width;
            _lastViewportHeight = Game1.uiViewport.Height;
            
            // Recenter window based on new dimensions
            xPositionOnScreen = (Game1.uiViewport.Width - chatWidth) / 2;
            yPositionOnScreen = (Game1.uiViewport.Height - chatHeight) / 2;
            
            // Update the window base dimensions
            width = chatWidth;
            height = chatHeight;
            
            InitializeComponents();
        }
    }

    // Supporting classes
    public class ChatMessage
    {
        public string Text { get; }
        public bool IsFromPlayer { get; }
        public bool IsTypingIndicator { get; }
        public DateTime Timestamp { get; }

        public ChatMessage(string text, bool isFromPlayer, bool isTypingIndicator = false)
        {
            Text = text;
            IsFromPlayer = isFromPlayer;
            IsTypingIndicator = isTypingIndicator;
            Timestamp = DateTime.Now;
        }
    }

    public class MessageActionButtons
    {
        public int MessageIndex { get; set; }
        public ClickableTextureComponent? EditButton { get; set; }
        public ClickableTextureComponent? DeleteButton { get; set; }
        public ClickableTextureComponent? RegenerateButton { get; set; }
        
        public MessageActionButtons(int messageIndex)
        {
            MessageIndex = messageIndex;
        }
    }

    public class ChatTextReceiver : IKeyboardSubscriber
    {
        private readonly ChatUI chatUI;

        public ChatTextReceiver(ChatUI chatUI)
        {
            this.chatUI = chatUI;
        }

        public bool Selected { get; set; } = true;

        public void RecieveTextInput(char inputChar)
        {
            if (!char.IsControl(inputChar))
            {
                chatUI.ReceiveTextInput(inputChar);
            }
        }

        public void RecieveTextInput(string text)
        {
            foreach (char c in text)
            {
                if (!char.IsControl(c))
                {
                    chatUI.ReceiveTextInput(c);
                }
            }
        }

        public void RecieveCommandInput(char command)
        {
            switch (command)
            {
                case '\r':
                case '\n':
                    chatUI.TrySendMessage();
                    break;
                case '\b':
                    chatUI.HandleBackspace();
                    break;
                case '\u001b': // Escape
                    if (Game1.activeClickableMenu is EditMessageModal)
                    {
                        chatUI.monitor.Log("ESC pressed but EditMessageModal is active, ignoring", LogLevel.Debug);
                        return;
                    }
                    chatUI.CleanupAndExit();
                    break;
            }
        }

        public void RecieveSpecialInput(Keys key)
        {
            if (key == Keys.Escape)
            {
                if (Game1.activeClickableMenu is EditMessageModal)
                {
                    chatUI.monitor.Log("ESC key pressed but EditMessageModal is active, ignoring", LogLevel.Debug);
                    return;
                }
                chatUI.CleanupAndExit();
            }
        }
    }
}