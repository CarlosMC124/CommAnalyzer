using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace CommAnalyzer.Helpers
{
    public class CSoporte
    {
        public class ComboItem
        {
            public string Key { get; set; }
            public string Value { get; set; }
            public ComboItem()
            {

            }
            public ComboItem(string puertoCOM, string nombreCompletoCOM)
            {
                Key = puertoCOM; Value = nombreCompletoCOM;
            }
            public override string ToString()
            {
                return Value;
            }
        }

        public static List<ComboItem> PuertoSerieNombreCompleto()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption like '%(COM%'"))
            {
                var ports = searcher.Get().Cast<ManagementBaseObject>().ToList().Select(p => p["Caption"].ToString());

                // Obtenemos los valores de Key y Value
                var portNamesKeys = SerialPort.GetPortNames();
                var portNameValues = portNamesKeys.Select(n =>/* n + " - " + */ports.FirstOrDefault(s => s.Contains("(" + n))).ToList();    //Agregamos (+COMX asi para los COM de simulacion que viene COM1->COM2 sino al COM2 le pone el mismo texto


                // Usamos otra alternativa para juntar 2 arrays en uno solo, y de esa forma utilizar un solo foreach
                var portNames = portNamesKeys.Zip(portNameValues, (k, v) => new { Key = k, Value = v });

                List<ComboItem> listPortComboItem = new List<ComboItem>();

                foreach (var portName in portNames)
                {
                    ComboItem portComboItem = new ComboItem();
                    portComboItem.Value = portName.Value;
                    portComboItem.Key = portName.Key;
                    listPortComboItem.Add(portComboItem);
                }
                return listPortComboItem;
            }
        }
    }
}
