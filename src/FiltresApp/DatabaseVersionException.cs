namespace FiltresApp;

/// <summary>Logiciel et base de données à des versions incompatibles : le démarrage est bloqué.</summary>
public class DatabaseVersionException(string message) : Exception(message);
