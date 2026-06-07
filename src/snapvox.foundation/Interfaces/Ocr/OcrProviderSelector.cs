using System;
using System.Collections.Generic;
using System.Linq;

namespace snapvox.foundation.interfaces.Ocr
{
    public static class OcrProviderSelector
    {
        public static IOcrProvider Select(IEnumerable<IOcrProvider> providers, string configuredEngine)
        {
            var availableProviders = providers?.Where(provider => provider != null).ToList() ?? new List<IOcrProvider>();
            if (availableProviders.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(configuredEngine))
            {
                var configured = availableProviders.FirstOrDefault(provider =>
                    string.Equals(provider.DisplayName, configuredEngine, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(provider.EngineId, configuredEngine, StringComparison.OrdinalIgnoreCase));
                if (configured != null)
                {
                    return configured;
                }
            }

            return availableProviders.FirstOrDefault(provider => provider.HasRequiredLanguages()) ?? availableProviders[0];
        }
    }
}
