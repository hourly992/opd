using UnityEngine;

public class StreetSession : MonoBehaviour
{
    public Font font;
    private GUIStyle title, small, button;

    private void OnGUI()
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { font = font, fontSize = 24, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14 };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 19 };
        }
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
        GUI.color = new Color(.05f,.09f,.12f,.7f);
        GUI.DrawTexture(new Rect(18,18,310,77), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(32,26,310,35), "Дунина-Горкавича", title);
        GUI.Label(new Rect(33,61,300,25), "Ханты-Мансийск · первый участок", small);
        GUI.Label(new Rect(24,677,800,30), "WASD / стрелки — идти     Мышь — обзор     Esc — меню", small);
        GUI.Label(new Rect(1000,687,270,25), "Карта: © OpenStreetMap contributors", small);

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            GUI.color = new Color(.025f,.045f,.065f,.9f);
            GUI.DrawTexture(new Rect(438,240,404,221), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(474,260,340,40), "Прогулка по улице", title);
            if (GUI.Button(new Rect(478,318,324,49), "Продолжить", button))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (GUI.Button(new Rect(478,386,324,49), "Выйти из игры", button)) Application.Quit();
        }
    }
}
