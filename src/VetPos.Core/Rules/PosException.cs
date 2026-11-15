namespace VetPos.Core.Rules;

/// <summary>
/// A problem the cashier can fix, with a plain message for the screen ("Not enough stock: only 3
/// left"). Technical detail goes to the log only, never to this message.
/// </summary>
public class PosException(string message) : Exception(message);
