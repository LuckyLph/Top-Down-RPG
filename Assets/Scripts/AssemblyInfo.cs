using System.Runtime.CompilerServices;

// Test hooks are internal rather than private-and-reflected.
[assembly: InternalsVisibleTo("TopDownRPG.EditModeTests")]
[assembly: InternalsVisibleTo("TopDownRPG.PlayModeTests")]
