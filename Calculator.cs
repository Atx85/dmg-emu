
using Gtk;
using System;
using System.Data; // For quick expression evaluation (not recommended for production)

public class Calculator : Window
{
    Entry display;
    string currentExpression = "";

    public Calculator() : base("GTK# Calculator")
    {
        SetDefaultSize(300, 400);
        SetPosition(WindowPosition.Center);

        VBox vbox = new VBox(false, 2);

        display = new Entry();
        display.Editable = false;
        display.Text = "0";
        vbox.PackStart(display, false, false, 0);

        Table table = new Table(5, 4, true);

        string[,] buttons = {
            { "7", "8", "9", "/" },
            { "4", "5", "6", "*" },
            { "1", "2", "3", "-" },
            { "0", ".", "=", "+" },
            { "C", "", "", "" }
        };

        for (uint i = 0; i < 5; i++)
        {
            for (uint j = 0; j < 4; j++)
            {
                string label = buttons[i, j];
                if (string.IsNullOrEmpty(label)) continue;

                Button btn = new Button(label);
                btn.Clicked += OnButtonClicked;
                table.Attach(btn, j, j + 1, i, i + 1);
            }
        }

        vbox.PackStart(table, true, true, 0);

        Add(vbox);
        DeleteEvent += (o, args) => Application.Quit();
        ShowAll();
    }

    void OnButtonClicked(object sender, EventArgs e)
    {
        Button btn = sender as Button;
        string val = btn.Label;

        switch (val)
        {
            case "=":
                EvaluateExpression();
                break;
            case "C":
                currentExpression = "";
                display.Text = "0";
                break;
            default:
                currentExpression += val;
                display.Text = currentExpression;
                break;
        }
    }

    void EvaluateExpression()
    {
        try
        {
            var dt = new DataTable();
            var result = dt.Compute(currentExpression, "");
            display.Text = result.ToString();
            currentExpression = result.ToString();
        }
        catch
        {
            display.Text = "Error";
            currentExpression = "";
        }
    }

    public static void Main()
    {
        Application.Init();
        new Calculator();
        Application.Run();
    }
}
