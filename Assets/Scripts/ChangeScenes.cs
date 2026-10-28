using UnityEngine;
using UnityEngine.SceneManagement;

public class ChangeScenes : MonoBehaviour
{
    public void BuoyancyPractical()
    {
        SceneManager.LoadScene("BuoyancyPractical");
    }
    
    public void WavesPractical()
    {
        SceneManager.LoadScene("WavesPractical");
    }

    public void Settings()
    {
        SceneManager.LoadScene("SettingsScene");
    }
    
    public void MainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
}
