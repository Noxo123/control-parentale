using System.Drawing.Drawing2D;

namespace NoxoParental;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Noxo Parental", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Application.Run(new RoleForm());
    }
}

internal enum AppRole { Parent, Child }

internal sealed class RoleForm : Form
{
    public RoleForm()
    {
        Text = "Noxo Parental";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 440);
        MinimumSize = new Size(640, 400);
        BackColor = Color.FromArgb(247, 249, 252);
        Font = new Font("Segoe UI", 10F);
        Build();
    }

    void Build()
    {
        var title = new Label { Text = "Noxo Parental", Font = new Font("Segoe UI", 26, FontStyle.Bold), AutoSize = true, Location = new Point(48, 38) };
        var subtitle = new Label { Text = "Grandir avec un numérique équilibré.", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(51, 82) };
        Controls.AddRange([title, subtitle]);
        AddRole("👨‍👩‍👦  Parent", "Accompagner, comprendre et régler les habitudes numériques.", AppRole.Parent, 48, 145);
        AddRole("🧒  Enfant", "Voir son temps, ses pauses et son équilibre numérique.", AppRole.Child, 365, 145);
    }

    void AddRole(string title, string text, AppRole role, int x, int y)
    {
        var panel = new Panel { Location = new Point(x, y), Size = new Size(290, 190), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var t = new Label { Text = title, Font = new Font("Segoe UI", 17, FontStyle.Bold), AutoSize = true, Location = new Point(20, 22) };
        var d = new Label { Text = text, AutoSize = false, Size = new Size(245, 65), Location = new Point(20, 62), ForeColor = Color.DimGray };
        var b = new Button { Text = role == AppRole.Parent ? "Ouvrir l'espace parent" : "Ouvrir mon espace", Location = new Point(20, 135), Size = new Size(245, 38), FlatStyle = FlatStyle.Flat };
        b.Click += (_, _) => { Hide(); new DashboardForm(role).ShowDialog(this); Show(); };
        panel.Controls.AddRange([t, d, b]); Controls.Add(panel);
    }
}

internal sealed class DashboardForm : Form
{
    readonly AppRole role;
    readonly Panel content = new();
    Label heading = new();
    Label summary = new();

    public DashboardForm(AppRole role)
    {
        this.role = role;
        Text = role == AppRole.Parent ? "Noxo Parental — Espace parent" : "Noxo Parental — Mon espace";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1050, 680);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(247, 249, 252);
        Font = new Font("Segoe UI", 10F);
        Build();
        ShowHome();
    }

    void Build()
    {
        var nav = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = Color.FromArgb(30, 39, 56), Padding = new Padding(18) };
        var logo = new Label { Text = "NOXO\nPARENTAL", ForeColor = Color.White, Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = true, Location = new Point(18, 25) };
        nav.Controls.Add(logo);
        string[] items = role == AppRole.Parent ? ["Accueil", "Temps", "Planning", "Applications", "Bien-être", "Réglages"] : ["Aujourd'hui", "Mon temps", "Mon planning", "Mes pauses", "Mes activités"];
        for (int i = 0; i < items.Length; i++)
        {
            var b = new Button { Text = items[i], Tag = i, Width = 180, Height = 42, Location = new Point(18, 115 + i * 48), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(44, 55, 76), TextAlign = ContentAlignment.MiddleLeft };
            int page = i; b.Click += (_, _) => ShowPage(page); nav.Controls.Add(b);
        }
        Controls.Add(nav);
        content.Dock = DockStyle.Fill; content.Padding = new Padding(35); Controls.Add(content);
    }

    void ShowPage(int page)
    {
        content.Controls.Clear();
        heading = new Label { Text = role == AppRole.Parent ? new[] { "Bonjour 👋", "Temps numérique", "Planning", "Applications", "Bien-être", "Réglages" }[page] : new[] { "Bonjour 👋", "Mon temps", "Mon planning", "Mes pauses", "Mes activités" }[page], Font = new Font("Segoe UI", 25, FontStyle.Bold), AutoSize = true };
        content.Controls.Add(heading);
        var card = new Panel { Location = new Point(35, 105), Size = new Size(700, 190), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        summary = new Label { Text = role == AppRole.Parent ? "Ici, vous accompagnez les habitudes numériques sans culpabiliser.\n\nConfigurez progressivement des temps de jeu, de repos et d'activités.\nLes règles doivent rester compréhensibles et adaptées à l'enfant." : "Aujourd'hui, votre objectif est l'équilibre.\n\nVous pouvez voir votre temps d'écran, prévoir une pause et suivre votre journée.\nPas de jugement : juste des repères pour mieux gérer votre temps.", AutoSize = false, Size = new Size(630, 145), Location = new Point(30, 25), Font = new Font("Segoe UI", 11F), ForeColor = Color.FromArgb(65, 72, 85) };
        card.Controls.Add(summary); content.Controls.Add(card);
        if (role == AppRole.Parent) AddParentControls(page); else AddChildControls(page);
    }

    void ShowHome() => ShowPage(0);

    void AddParentControls(int page)
    {
        if (page == 0) AddButton("Ajouter un appareil", "Associer un PC enfant avec un code.", 35, 330);
        if (page == 1) AddButton("Définir un objectif", "Choisir une durée quotidienne adaptée.", 35, 330);
        if (page == 2) AddButton("Créer un créneau", "Prévoir jeu, devoirs, sommeil et pauses.", 35, 330);
        if (page == 3) AddButton("Gérer les applications", "Choisir les applications à suivre.", 35, 330);
        if (page == 4) AddButton("Ajouter un rappel", "Encourager les pauses et le sommeil.", 35, 330);
    }

    void AddChildControls(int page)
    {
        if (page == 0) AddButton("Voir ma journée", "Un aperçu simple de mon équilibre numérique.", 35, 330);
        if (page == 1) AddButton("Voir mon temps", "Comprendre où passe mon temps.", 35, 330);
        if (page == 3) AddButton("Planifier une pause", "Choisir quand faire une pause.", 35, 330);
    }

    void AddButton(string title, string text, int x, int y)
    {
        var b = new Button { Text = title + "\n" + text, Location = new Point(x, y), Size = new Size(700, 72), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(20), Font = new Font("Segoe UI", 10F) };
        b.Click += (_, _) => MessageBox.Show("Cette fonction sera activée dans la prochaine étape.\n\nPour l'instant, nous construisons l'interface sans activer de blocage.", "Noxo Parental", MessageBoxButtons.OK, MessageBoxIcon.Information);
        content.Controls.Add(b);
    }
}
