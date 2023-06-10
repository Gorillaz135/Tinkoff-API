using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace RcktMon.Converters
{
    public class CurrencyNameToHeaderConverter : IValueConverter
    {
        static Dictionary<string, string> _headers = new Dictionary<string, string>
        {
            { "RUB", "Акции РФ" }
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && _headers.TryGetValue(str.ToUpper(), out var val))
                return val;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
