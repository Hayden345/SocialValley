using System.Collections.Generic;

namespace SocialValley
{
    public enum AIProvider
    {
        Player2,
        Ollama,
        OpenRouter,
        Google,
        OpenAI,
        None
    }

    public struct ProviderDef
    {
        public string Label;
        public string EndpointUrl;
        public string ListModelsUrl;
        public Dictionary<string, string> ExtraHeaders;
        public bool RequiresApiKey;
    }

    public static class AIProviderRegistry
    {
        public static readonly Dictionary<AIProvider, ProviderDef> Defs = new()
        {
            {
                AIProvider.Player2, new ProviderDef
                {
                    Label = "Player2",
                    EndpointUrl = "https://api.player2.game/v1/chat/completions",
                    RequiresApiKey = true
                }
            },
            {
                AIProvider.Ollama, new ProviderDef
                {
                    Label = "Ollama",
                    EndpointUrl = "http://localhost:11434/api/chat",
                    ListModelsUrl = "http://localhost:11434/api/tags",
                    RequiresApiKey = false
                }
            },
            {
                AIProvider.OpenRouter, new ProviderDef
                {
                    Label = "OpenRouter",
                    EndpointUrl = "https://openrouter.ai/api/v1/chat/completions",
                    ListModelsUrl = "https://openrouter.ai/api/v1/models",
                    RequiresApiKey = true
                }
            },
            {
                AIProvider.Google, new ProviderDef
                {
                    Label = "Google Gemini",
                    EndpointUrl = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                    ListModelsUrl = "https://generativelanguage.googleapis.com/v1beta/models",
                    RequiresApiKey = true
                }
            },
            {
                AIProvider.OpenAI, new ProviderDef
                {
                    Label = "OpenAI",
                    EndpointUrl = "https://api.openai.com/v1/chat/completions",
                    ListModelsUrl = "https://api.openai.com/v1/models",
                    RequiresApiKey = true
                }
            }
        };
        
        public static string GetLabel(this AIProvider p)
        {
            if (Defs.TryGetValue(p, out var def) && !string.IsNullOrEmpty(def.Label))
            {
                return def.Label;
            }
            return p.ToString();
        }

        public static string GetEndpointUrl(this AIProvider p)
        {
            return Defs.TryGetValue(p, out var def) ? def.EndpointUrl : null;
        }

        public static string GetListModelsUrl(this AIProvider p)
        {
            return Defs.TryGetValue(p, out var def) ? def.ListModelsUrl : null;
        }

        public static bool RequiresApiKey(this AIProvider p)
        {
            return Defs.TryGetValue(p, out var def) && def.RequiresApiKey;
        }

        public static Dictionary<string, string> GetExtraHeaders(this AIProvider p)
        {
            return Defs.TryGetValue(p, out var def) ? def.ExtraHeaders : null;
        }
    }
}