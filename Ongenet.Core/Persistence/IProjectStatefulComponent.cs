namespace Ongenet.Core.Persistence;

/// <summary>
/// Implemented by an instrument or effect whose state is NOT fully captured by its generic
/// <c>Parameters</c> list (e.g. the EQ's variable band list). The project serializer writes/reads this
/// extra state as a length-prefixed blob, so a reader that doesn't understand it can skip it.
/// </summary>
public interface IProjectStatefulComponent
{
    void WriteProjectState(OngenWriter writer);
    void ReadProjectState(OngenReader reader);
}
