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

internal static class Ui
{
    public static Color Bg => Color.FromArgb(246, 248, 252);
    public static Color Surface => Color.White;
    public static Color Text => Color.FromArgb(28, 32, 40);
    public static Color Muted => Color.FromArgb(105, 112, 125);
    public static Color Accent => Color.FromArgb(42, 102, 240);
    public static Color AccentSoft => Color.FromArgb(232, 239, 255);
    public static Color Nav => Color.FromArgb(31, 36, 48);
    public static Font Title(float size = 26) => new("Segoe UI", size, FontStyle.Bold);
    public static Font Body(float size = 10) => new("Segoe UI", size);
    public static Button Button(string text) => new() { Text = text, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = Accent, ForeColor = Color.White, Cursor = Cursors.Hand, Font = Body(10), TabStop = false };
}

internal sealed class RoleForm : Form
{
    public RoleForm()
    {
        Text = "Noxo Parental";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 560);
        MinimumSize = new Size(760, 500);
        BackColor = Ui.Bg;
        Font = Ui.Body();
        Build();
    }

    void Build()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(48, 40, 48, 40) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        Controls.Add(root);

        var title = new Label { Text = "Noxo Parental", Font = Ui.Title(30), ForeColor = Ui.Text, Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(title, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 10, 0, 10) };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.Controls.Add(cards, 0, 1);
        cards.Controls.Add(RoleCard(AppRole.Parent, "👨‍👩‍👦", "Espace parent", "Accompagner le quotidien numérique avec des repères simples."), 0, 0);
        cards.Controls.Add(RoleCard(AppRole.Child, "🌱", "Mon espace", "Comprendre mon temps, mes pauses et mon équilibre."), 1, 0);

        root.Controls.Add(new Label { Text = "Un outil pour accompagner, pas pour culpabiliser.", ForeColor = Ui.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
    }

    Control RoleCard(AppRole role, string icon, string title, string text)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Surface, Margin = new Padding(10), Padding = new Padding(28) };
        card.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(225, 229, 238)); e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1); };
        card.Controls.Add(new Label { Text = icon, Font = new Font("Segoe UI Emoji", 28), AutoSize = true, Location = new Point(28, 25) });
        card.Controls.Add(new Label { Text = title, Font = Ui.Title(20), ForeColor = Ui.Text, AutoSize = true, Location = new Point(28, 82) });
        card.Controls.Add(new Label { Text = text, ForeColor = Ui.Muted, AutoSize = false, Size = new Size(320, 65), Location = new Point(28, 125) });
        var button = Ui.Button(role == AppRole.Parent ? "Ouvrir l'espace parent" : "Ouvrir mon espace");
        button.Location = new Point(28, 205); button.Size = new Size(270, 44);
        button.Click += (_, _) => { using var dashboard = new DashboardForm(role); Hide(); dashboard.ShowDialog(this); Show(); };
        card.Controls.Add(button);
        return card;
    }
}

internal sealed class DashboardForm : Form
{
    readonly AppRole role;
    readonly Panel content = new();
    readonly FlowLayoutPanel nav = new();
    Label heading = new();
    int activePage;
    string[] pages = Array.Empty<string>();

    public DashboardForm(AppRole role)
    {
        this.role = role;
        pages = role == AppRole.Parent
            ? ["Accueil", "Temps", "Planning", "Activités", "Bien-être", "Réglages"]
            : ["Aujourd'hui", "Mon temps", "Mon planning", "Mes pauses", "Mes activités"];
        Text = role == AppRole.Parent ? "Noxo Parental — Parent" : "Noxo Parental — Enfant";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(900, 620);
        BackColor = Ui.Bg;
        Font = Ui.Body();
        Build();
        ShowPage(0);
    }

    void Build()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Nav, Padding = new Padding(18) };
        root.Controls.Add(sidebar, 0, 0);
        sidebar.Controls.Add(new Label { Text = "NOXO\nPARENTAL", ForeColor = Color.White, Font = Ui.Title(18), AutoSize = true, Location = new Point(18, 20) });
        sidebar.Controls.Add(new Label { Text = role == AppRole.Parent ? "ESPACE PARENT" : "MON ESPACE", ForeColor = Color.FromArgb(170, 180, 200), Font = Ui.Body(8), AutoSize = true, Location = new Point(20, 78) });

        nav.Dock = DockStyle.Fill; nav.FlowDirection = FlowDirection.TopDown; nav.WrapContents = false; nav.AutoScroll = true; nav.Padding = new Padding(0, 115, 0, 10); nav.BackColor = Color.Transparent;
        sidebar.Controls.Add(nav);
        for (var i = 0; i < pages.Length; i++) AddNavButton(i);

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(36, 30, 36, 30), AutoScroll = true };
        root.Controls.Add(main, 1, 0);
        main.Controls.Add(content);
        content.Dock = DockStyle.Fill;
        content.AutoScroll = true;
    }

    void AddNavButton(int index)
    {
        var button = new Button { Text = "  " + pages[index], Width = 198, Height = 44, Margin = new Padding(0, 0, 0, 7), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = Color.Transparent, ForeColor = Color.FromArgb(215, 220, 232), TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand, Tag = index };
        button.Click += (_, _) => ShowPage(index);
        nav.Controls.Add(button);
    }

    void ShowPage(int page)
    {
        activePage = page;
        content.SuspendLayout();
        content.Controls.Clear();
        heading = new Label { Text = pages[page], Font = Ui.Title(28), ForeColor = Ui.Text, Dock = DockStyle.Top, Height = 55 };
        content.Controls.Add(heading);
        var intro = new Label { Text = role == AppRole.Parent ? ParentIntro(page) : ChildIntro(page), ForeColor = Ui.Muted, Dock = DockStyle.Top, Height = 55 };
        content.Controls.Add(intro);

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 440, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 15, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        content.Controls.Add(grid);
        AddCard(grid, 0, 0, CardTitle(page, 0), CardText(page, 0), "Voir");
        AddCard(grid, 1, 0, CardTitle(page, 1), CardText(page, 1), "Gérer");
        AddCard(grid, 0, 1, CardTitle(page, 2), CardText(page, 2), "Modifier");
        AddCard(grid, 1, 1, CardTitle(page, 3), CardText(page, 3), "Découvrir");
        content.ResumeLayout(true);
    }

    void AddCard(TableLayoutPanel grid, int col, int row, string title, string text, string action)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Surface, Margin = new Padding(8), Padding = new Padding(22) };
        card.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(225, 229, 238)); e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1); };
        card.Controls.Add(new Label { Text = title, Font = Ui.Title(15), ForeColor = Ui.Text, AutoSize = true, Location = new Point(22, 20) });
        card.Controls.Add(new Label { Text = text, ForeColor = Ui.Muted, AutoSize = false, Size = new Size(300, 55), Location = new Point(22, 55) });
        var button = Ui.Button(action); button.Size = new Size(115, 36); button.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; button.Location = new Point(22, Math.Max(105, card.Height - 58));
        button.Click += (_, _) => ShowFriendlyMessage(title);
        card.Controls.Add(button); grid.Controls.Add(card, col, row);
    }

    void ShowFriendlyMessage(string title) => MessageBox.Show($"{title}\n\nCette partie est prête pour la prochaine étape.\nAucune action de blocage n'est exécutée.", "Noxo Parental", MessageBoxButtons.OK, MessageBoxIcon.Information);

    string ParentIntro(int p) => p switch { 0 => "Un aperçu calme des habitudes et des objectifs de la famille.", 1 => "Créer des repères de temps adaptés, sans culpabiliser.", 2 => "Organiser les moments de jeu, repos, école et sommeil.", 3 => "Comprendre les activités numériques avant de définir des règles.", 4 => "Encourager les pauses, le sommeil et un usage équilibré.", _ => "Personnaliser l'expérience familiale." };
    string ChildIntro(int p) => p switch { 0 => "Voici ta journée : des repères simples pour garder ton équilibre.", 1 => "Comprends ton temps d'écran et ce que tu peux ajuster.", 2 => "Prépare ta journée avec des moments pour jouer, apprendre et te reposer.", 3 => "Les pauses sont là pour t'aider, pas pour te punir.", _ => "Découvre tes activités et tes habitudes numériques." };
    string CardTitle(int p, int i) => role == AppRole.Parent ? new[] { "Aujourd'hui", "Objectif quotidien", "Planning", "Dernières activités" }[i] : new[] { "Mon équilibre", "Temps utilisé", "Prochaine pause", "Mes activités" }[i];
    string CardText(int p, int i) => role == AppRole.Parent ? new[] { "Aucun appareil associé pour le moment.", "Configurez progressivement un objectif.", "Aucun créneau configuré.", "Les statistiques apparaîtront ici." }[i] : new[] { "Ton espace personnel, sans jugement.", "Tes données seront visibles ici.", "Aucune pause programmée.", "Tes activités apparaîtront ici." }[i];
}
