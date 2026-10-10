// Real units: m/s², kg/m³, mPa·s, m³ and kg
public interface ISImulationAPI
{
    // General
    public float GetGravity();
    public void SetGravity(float gravity);

    // Fluid
    public float GetDensity();
    public void SetDensity(float density);
    public float GetViscosity();
    public void SetViscosity(float viscosity);

    // Bodies
    public int AddBody(BodyShape3D shape, float volume, float mass);
    public void SetBodyMass(int id, float mass);
    public void SetBodyShape(int id, BodyShape3D shape, float volume);
    public void RemoveBody(int id);
}
