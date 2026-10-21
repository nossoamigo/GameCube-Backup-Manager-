#region Using

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using sio = System.IO;

#endregion

namespace GCBM;

/// <summary>
///     Seleção de jogos via arquivo de lista de códigos (.sel), no estilo do Wii Backup Manager.
///     Formato: um ID de jogo por linha (ex.: GALE01). Linhas vazias e linhas iniciadas por
///     '#' ou ';' são ignoradas. Qualquer texto após o ID (ex.: "GALE01 = Super Smash Bros. Melee")
///     também é ignorado. IDs de 4 caracteres (ex.: GALE) selecionam todas as variantes.
/// </summary>
public partial class frmMain
{
    private const int SEL_CHECK_COLUMN = 0;
    private const string SEL_ID_COLUMN = "ID";
    private const string SEL_FILE_FILTER = "Lista de seleção (*.sel;*.txt)|*.sel;*.txt|Todos os arquivos (*.*)|*.*";

    private static bool IsPortugueseUI =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt";

    private static string SelText(string pt, string en) => IsPortugueseUI ? pt : en;

    #region Menu setup

    /// <summary>
    ///     Adiciona "Carregar seleção (.sel)" e "Salvar seleção (.sel)" aos submenus "Selecionar"
    ///     das listas de origem e de destino. Chamado após InitializeComponent().
    /// </summary>
    private void InitSelectionFileMenus()
    {
        AddSelFileItems(tsmiSelectGameList, () => dgvSource);
        AddSelFileItems(tsmiSelectGameDisc, () => dgvDestination);
    }

    private void AddSelFileItems(ToolStripMenuItem parent, Func<DataGridView> grid)
    {
        if (parent == null) return;

        // Evita duplicar os itens se o InitializeComponent() for chamado de novo.
        if (parent.DropDownItems.ContainsKey(parent.Name + "_LoadSel")) return;

        var load = new ToolStripMenuItem(SelText("Carregar seleção (.sel)...", "Load selection (.sel)..."))
        {
            Name = parent.Name + "_LoadSel"
        };
        load.Click += (_, _) => LoadSelectionFile(grid());

        var save = new ToolStripMenuItem(SelText("Salvar seleção (.sel)...", "Save selection (.sel)..."))
        {
            Name = parent.Name + "_SaveSel"
        };
        save.Click += (_, _) => SaveSelectionFile(grid());

        parent.DropDownItems.Add(new ToolStripSeparator());
        parent.DropDownItems.Add(load);
        parent.DropDownItems.Add(save);
    }

    #endregion

    #region Parsing

    /// <summary>
    ///     Lê um arquivo .sel e devolve os IDs encontrados (em maiúsculas, sem duplicatas,
    ///     na ordem em que aparecem).
    /// </summary>
    internal static List<string> ReadSelectionFile(string path)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        char[] separators = { ' ', '\t', ',', ';', '=', '|', '[', ']', '(', ')' };

        foreach (var rawLine in sio.File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("//"))
                continue;

            var token = line.Split(separators, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (token == null) continue;

            token = token.ToUpperInvariant();
            if ((token.Length == 4 || token.Length == 6) && token.All(char.IsLetterOrDigit) && seen.Add(token))
                ids.Add(token);
        }

        return ids;
    }

    private static string GetRowGameId(DataGridViewRow row)
    {
        var value = row.DataGridView.Columns.Contains(SEL_ID_COLUMN)
            ? row.Cells[SEL_ID_COLUMN].Value
            : row.Cells[2].Value; // Fallback: ID é a 3ª coluna (ver PopDgv)
        return value?.ToString().Trim().ToUpperInvariant() ?? string.Empty;
    }

    private static bool IsRowChecked(DataGridViewRow row)
    {
        var v = row.Cells[SEL_CHECK_COLUMN].Value;
        return v is bool b ? b : bool.TryParse(v?.ToString(), out var parsed) && parsed;
    }

    #endregion

    #region Load / Save

    /// <summary>
    ///     Abre um .sel e marca, na lista informada, todos os jogos cujo ID consta no arquivo.
    ///     Os demais jogos são desmarcados (mesmo comportamento do WBM).
    /// </summary>
    private void LoadSelectionFile(DataGridView dgv)
    {
        if (dgv == null || dgv.Rows.Count == 0)
        {
            _ = MessageBox.Show(SelText("A lista está vazia. Carregue os jogos primeiro.",
                    "The list is empty. Load the games first."),
                "GCBM", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Filter = SEL_FILE_FILTER,
            Title = SelText("Carregar seleção", "Load selection"),
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        List<string> ids;
        try
        {
            ids = ReadSelectionFile(ofd.FileName);
        }
        catch (Exception ex)
        {
            _ = MessageBox.Show(ex.Message, "GCBM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var fullIds = new HashSet<string>(ids.Where(i => i.Length == 6));
        var shortIds = ids.Where(i => i.Length == 4).ToList();
        var matchedIds = new HashSet<string>();
        var checkedCount = 0;

        // Confirma a edição em andamento para a célula de checkbox não "voltar" ao valor anterior.
        dgv.EndEdit();

        foreach (DataGridViewRow row in dgv.Rows)
        {
            var id = GetRowGameId(row);
            var match = false;

            if (id.Length > 0)
            {
                if (fullIds.Contains(id))
                {
                    match = true;
                    matchedIds.Add(id);
                }
                else
                {
                    var prefix = shortIds.FirstOrDefault(s => id.StartsWith(s, StringComparison.Ordinal));
                    if (prefix != null)
                    {
                        match = true;
                        matchedIds.Add(prefix);
                    }
                }
            }

            row.Cells[SEL_CHECK_COLUMN].Value = match;
            if (match) checkedCount++;
        }

        dgv.Refresh();

        var notFound = ids.Where(i => !matchedIds.Contains(i)).ToList();
        var msg = new StringBuilder();
        msg.AppendLine(SelText(
            $"{checkedCount} jogo(s) selecionado(s) a partir de {ids.Count} código(s) do arquivo.",
            $"{checkedCount} game(s) selected from {ids.Count} code(s) in the file."));

        if (notFound.Count > 0)
        {
            msg.AppendLine();
            msg.AppendLine(SelText($"Não encontrados na lista ({notFound.Count}):",
                $"Not found in the list ({notFound.Count}):"));
            msg.AppendLine(string.Join(", ", notFound.Take(40)));
            if (notFound.Count > 40) msg.AppendLine("...");
        }

        _ = MessageBox.Show(msg.ToString(), "GCBM", MessageBoxButtons.OK,
            notFound.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    /// <summary>
    ///     Salva os IDs dos jogos marcados na lista informada em um arquivo .sel.
    /// </summary>
    private void SaveSelectionFile(DataGridView dgv)
    {
        if (dgv == null) return;
        dgv.EndEdit();

        var ids = dgv.Rows.Cast<DataGridViewRow>()
            .Where(IsRowChecked)
            .Select(GetRowGameId)
            .Where(id => id.Length > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            _ = MessageBox.Show(SelText("Nenhum jogo está marcado.", "No games are checked."),
                "GCBM", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = SEL_FILE_FILTER,
            DefaultExt = "sel",
            AddExtension = true,
            FileName = "selection.sel",
            Title = SelText("Salvar seleção", "Save selection")
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            sio.File.WriteAllLines(sfd.FileName, ids, new UTF8Encoding(false));
            _ = MessageBox.Show(SelText($"{ids.Count} código(s) salvo(s).", $"{ids.Count} code(s) saved."),
                "GCBM", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _ = MessageBox.Show(ex.Message, "GCBM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion
}
