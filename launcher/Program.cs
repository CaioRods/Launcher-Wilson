using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class Central : Form
{
    private static readonly Color Paper = Color.FromArgb(255, 248, 235);
    private static readonly Color Ink = Color.FromArgb(52, 24, 20);
    private static readonly Color Red = Color.FromArgb(161, 24, 32);
    private static readonly Color Gold = Color.FromArgb(255, 196, 62);
    private readonly Button excel, word, both, check;
    private readonly CheckBox machine;
    private readonly Label status;
    private readonly ProgressBar progress;
    private readonly Update[] updates = new Update[2];
    private bool busy;
    private readonly System.Action<int> openInWorkspace;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SuiteHost.Run(args);
    }

    internal Central(System.Action<int> open = null)
    {
        openInWorkspace = open;
        Text = "Central de Apps · Alimentos Wilson";
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(690, 558);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Paper;
        var hero = new Hero { Bounds = new Rectangle(0, 0, 690, 171) };
        Controls.Add(hero);
        hero.Controls.Add(Art("wilson", 32, 17, 106, 52));
        hero.Controls.Add(Caption("Central de Apps", 32, 72, 600, 50, 30f, Color.White, true));
        hero.Controls.Add(Caption("Instale e atualize seus aplicativos de trabalho.", 34, 129, 625, 25, 11f, Color.White, false));

        excel = Tile("Excel", "Planilhas, cálculos e controles.", 32, Red, Color.White);
        word = Tile("Word", "Documentos, relatórios e modelos.", 351, Gold, Ink);

        machine = new CheckBox {
            Text = "Instalar para todos os usuários (requer administrador)",
            Bounds = new Rectangle(32, 356, 626, 27), ForeColor = Ink
        };
        Controls.Add(machine);
        machine.CheckedChanged += (s, e) => { if (!busy) RefreshButtons(); };
        both = Action("Instalar os dois", 32, 396, 307, 40, Red, Color.White);
        check = Action("Buscar atualizações", 351, 396, 307, 40, Color.FromArgb(249, 227, 182), Ink);
        Controls.AddRange(new Control[] { both, check });

        status = Caption("Windows 10 e 11 · x64  |  Instalação para seu usuário", 32, 460, 626, 52, 10f, Ink, false);
        status.AccessibleName = "Estado da instalação";
        Controls.Add(status);
        progress = new ProgressBar {
            Bounds = new Rectangle(32, 518, 626, 8), Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25, Visible = false, AccessibleName = "Operação em andamento"
        };
        Controls.Add(progress);
        excel.Click += async (s, e) => await Install(0);
        word.Click += async (s, e) => await Install(1);
        both.Click += async (s, e) => await Install(0, 1);
        check.Click += async (s, e) => await CheckUpdates();
        RefreshButtons();
        Activated += (s, e) => { if (!busy) RefreshButtons(); };
        FormClosing += (s, e) => { if (busy) e.Cancel = true; };
        if (SystemInformation.IsMenuAnimationEnabled && !SystemInformation.HighContrast)
        {
            Opacity = 0;
            Shown += (s, e) =>
            {
                var clock = Stopwatch.StartNew();
                var animation = new Timer { Interval = 15 };
                animation.Tick += (sender, args) =>
                {
                    double t = Math.Min(1.0, clock.ElapsedMilliseconds / 290.0);
                    Opacity = 1.0 - Math.Pow(1.0 - t, 3);
                    if (t >= 1.0) animation.Dispose();
                };
                FormClosed += (sender, args) => animation.Dispose();
                animation.Start();
            };
        }
    }

    private Button Tile(string app, string description, int x, Color accent, Color text)
    {
        var tile = new Panel { Bounds = new Rectangle(x, 194, 307, 146), BackColor = Color.White };
        Controls.Add(tile);
        tile.Controls.Add(Art(app.ToLowerInvariant(), 14, 16, 58, 58));
        tile.Controls.Add(Caption(app + " AW", 84, 16, 207, 31, 17f, Ink, true));
        tile.Controls.Add(Caption(description, 84, 52, 207, 34, 9.5f, Ink, false));
        var button = Action("Instalar " + app + " AW", 14, 96, 279, 35, accent, text);
        tile.Controls.Add(button);
        return button;
    }

    private void SetBusy(bool value)
    {
        busy = progress.Visible = value;
        excel.Enabled = word.Enabled = both.Enabled = check.Enabled = machine.Enabled = !value;
    }

    private void RefreshButtons()
    {
        bool first = Distribution.InstalledPath(0) != null;
        bool second = Distribution.InstalledPath(1) != null;
        excel.Text = Distribution.ButtonText(0, first, Candidate(0) != null);
        word.Text = Distribution.ButtonText(1, second, Candidate(1) != null);
        both.Text = Candidate(0) != null || Candidate(1) != null ? "Instalar / atualizar os dois"
            : first && second ? "Abrir os dois" : first || second ? "Instalar o aplicativo restante" : "Instalar os dois";
    }

    private Update Candidate(int app)
    {
        return Distribution.ChooseUpdate(app, Distribution.InstalledVersion(app, machine.Checked),
            Distribution.InstalledPath(app) != null, updates[app]);
    }

    private async Task CheckUpdates()
    {
        SetBusy(true);
        status.Text = "Buscando novas versões do Excel AW e Word AW…";
        int errors = 0;
        try
        {
            for (int app = 0; app < updates.Length; app++)
            {
                int index = app;
                try { updates[index] = await Task.Run(() => Distribution.Latest(index)); }
                catch (System.Net.WebException) { errors++; }
                catch (IOException) { errors++; }
            }
            RefreshButtons();
            bool available = Candidate(0) != null || Candidate(1) != null;
            status.Text = available ? "Atualização disponível. Escolha qual aplicativo quer atualizar."
                : errors == 0 ? "Nenhuma versão mais recente disponível." : "A busca ficou incompleta. Tente novamente quando a conexão voltar.";
            if (available && errors != 0) status.Text += " A busca do outro app ficou incompleta.";
        }
        finally { SetBusy(false); }
    }

    private async Task Install(params int[] apps)
    {
        SetBusy(true);
        bool restart = false;
        int finished = 0;
        string appName = "";
        bool openExisting = Distribution.InstalledPath(0) != null && Distribution.InstalledPath(1) != null
            && Candidate(0) == null && Candidate(1) == null;
        try
        {
            foreach (int app in apps)
            {
                appName = Distribution.Names[app];
                Update update = Candidate(app);
                string installed = Distribution.InstalledPath(app);
                if (update == null && installed != null)
                {
                    // Batch installation skips an app already installed; once both are installed it opens both.
                    if (apps.Length == 1 || openExisting)
                    {
                        if (openInWorkspace != null) openInWorkspace(app);
                        else Process.Start(new ProcessStartInfo(installed) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(installed) });
                    }
                    finished++;
                    continue;
                }
                string folder = null;
                try
                {
                    string path = Distribution.LocalPath(app);
                    if (update != null && update.Url != null)
                    {
                        status.Text = "Baixando e verificando " + appName + " " + update.Version + "…";
                        folder = Path.Combine(Path.GetTempPath(), "Wilson-" + Guid.NewGuid().ToString("N"));
                        path = await Task.Run(() => Distribution.Download(update, folder));
                    }
                    else if (!File.Exists(path))
                    {
                        throw new FileNotFoundException("Extraia todo o ZIP mantendo a pasta instaladores.");
                    }

                    bool allUsers = machine.Checked;
                    status.Text = "Instalando " + appName + (allUsers ? " para todos os usuários…" : " para seu usuário…");
                    int code = await Task.Run(() => RunInstaller(path, allUsers));
                    if (code == 3010 || code == 1641) restart = true;
                    else if (code != 0)
                    {
                        status.Text = code == 1602 ? "Instalação de " + appName + " cancelada."
                            : code == 1618 ? "Outra instalação está em andamento. Tente novamente."
                            : "Falha ao instalar " + appName + " (código " + code + ").";
                        return;
                    }
                    finished++;
                }
                finally
                {
                    if (folder != null && Directory.Exists(folder))
                    {
                        try { Directory.Delete(folder, true); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
            status.Text = "Pronto. Use os botões Abrir para acessar seus aplicativos.";
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            status.Text = error.NativeErrorCode == 1223 ? "Permissão de administrador não concedida." : "O Windows não conseguiu abrir o instalador.";
        }
        catch (System.Net.WebException) { status.Text = "Falha ao baixar " + appName + ". Verifique a conexão e tente novamente."; }
        catch (IOException error) { status.Text = error.Message; }
        catch (UnauthorizedAccessException) { status.Text = "O Windows bloqueou o acesso aos arquivos de instalação."; }
        finally
        {
            if (finished > 0 && finished < apps.Length) status.Text += " O Excel AW já foi instalado.";
            if (restart) status.Text += " Reinicie o computador para concluir.";
            SetBusy(false);
            RefreshButtons();
        }
    }

    private static int RunInstaller(string path, bool allUsers)
    {
        string scope = allUsers ? "ALLUSERS=1" : "ALLUSERS=2 MSIINSTALLPERUSER=1";
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
            "/i \x22" + path + "\x22 /qb! /norestart " + scope) { UseShellExecute = allUsers };
        if (allUsers) info.Verb = "runas";
        using (Process process = Process.Start(info))
        {
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    private static Label Caption(string text, int x, int y, int width, int height, float size, Color color, bool bold)
    {
        return new Label {
            Text = text, Bounds = new Rectangle(x, y, width, height),
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color, BackColor = Color.Transparent, UseMnemonic = false
        };
    }

    private static Button Action(string text, int x, int y, int width, int height, Color color, Color ink)
    {
        var button = new Button {
            Text = text, Bounds = new Rectangle(x, y, width, height), FlatStyle = FlatStyle.Flat,
            BackColor = color, ForeColor = ink, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, 0.15f);
        return button;
    }

    private static PictureBox Art(string name, int x, int y, int width, int height)
    {
        Bitmap bitmap;
        using (Stream stream = typeof(Central).Assembly.GetManifestResourceStream(name))
        using (Image image = Image.FromStream(stream)) bitmap = new Bitmap(image);
        var picture = new PictureBox {
            Bounds = new Rectangle(x, y, width, height), Image = bitmap,
            SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent
        };
        picture.Disposed += (s, e) => bitmap.Dispose();
        return picture;
    }

    private sealed class Hero : Panel
    {
        public Hero() { DoubleBuffered = true; }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            if (Width == 0 || Height == 0) return;
            using (var wash = new LinearGradientBrush(ClientRectangle, Color.FromArgb(114, 12, 22), Color.FromArgb(190, 33, 35), 16f))
                args.Graphics.FillRectangle(wash, ClientRectangle);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(92, Gold), 1.4f))
                for (int i = 0; i < 6; i++)
                    args.Graphics.DrawEllipse(pen, Width - 195 + i * 14, -167 + i * 11, 430, 365);
            using (var line = new SolidBrush(Gold))
                args.Graphics.FillRectangle(line, 0, Height - 4, Width, 4);
        }
    }
}
