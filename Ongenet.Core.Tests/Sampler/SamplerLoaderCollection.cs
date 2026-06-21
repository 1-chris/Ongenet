using Xunit;

namespace Ongenet.Core.Tests.Sampler;

/// <summary>
/// Test collection for classes that mutate the shared static <c>SamplerInstrument.Loader</c>. Placing them
/// in one collection makes xUnit run them serially, so they don't race over the global loader handle.
/// </summary>
[CollectionDefinition("SamplerStaticLoader")]
public sealed class SamplerLoaderCollection { }
