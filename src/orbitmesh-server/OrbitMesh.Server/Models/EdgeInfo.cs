namespace OrbitMesh.Server.Models;

public sealed class EdgeInfo
{
    public bool IsConnected { get; set; }

    public DateTime RegistrationDate { get; set; }

    public required EdgeDescription Description { get; set; }
}
