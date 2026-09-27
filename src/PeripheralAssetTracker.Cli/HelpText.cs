namespace PeripheralAssetTracker.Cli;

internal static class HelpText
{
    public static void Print(ConsoleUi ui) => ui.Line("""
        Peripheral Asset Tracker — inventário e rastreabilidade de mouses e teclados (Windows 10/11)

        Uso:
          pat                                   Menu interativo
          pat <comando> [argumentos] [opções]

        Comandos:
          scan [--all] [--details]              Escaneia os periféricos conectados (--all inclui integrados)
          register <nº do scan|fingerprint>     Cadastra um dispositivo conectado
                   [--type mouse|keyboard|other] [--name "Nome"] [--person <id>]
                   [--computer <hostname> | --expected-current]
          devices [--type T] [--person <id>] [--computer <hostname>]
                  [--unassigned] [--low] [--unexpected]
          device <#id|fingerprint>              Detalhes de um dispositivo
          search <termo>                        Pesquisa (fingerprint, nome, responsável, hostname, VID, PID, serial, fabricante)
          people [list | add <nome> [--department S] | search <termo> | devices <id>]
          assign <#id|fingerprint> [--person <id>] [--computer <hostname>]
          transfer <#id|fingerprint> [--person <id>] [--computer <hostname>] --reason "..." [--yes]
          computers                             Computadores conhecidos
          history [<#id|fingerprint>]           Histórico do dispositivo (sem argumento: movimentações recentes)
          export [--delimiter ;|,|tab]          Exporta o inventário para reports/inventory_YYYY-MM-DD_HHMMSS.csv
          discover [--all] [--dump arquivo.json] Diagnóstico da descoberta (somente leitura)
          help

        Opções globais:
          --data-dir <pasta>    Pasta raiz de data/, logs/ e reports/ (padrão: pasta atual ou PAT_DATA_DIR)
          --fixture <arq.json>  Simulação: usa um snapshot PnP em vez do hardware real
          --host <nome>         Simulação: usa outro hostname (validar Expected vs Observed)
        """);
}
