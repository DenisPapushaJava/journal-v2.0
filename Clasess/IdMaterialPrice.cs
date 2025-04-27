namespace Jornal
{
    internal class IdMaterialPrice
    {
        static public string NameTableDBOfId(string id)
        {
            string nameTable = "PriceLL500";
            if (id != null)
            {
                if (id == "1")
                {
                    nameTable = "PriceHL_id1";
                }
                if (id == "2")
                {
                    nameTable = "PriceHL_id2";
                }
            }
            return nameTable;
        }
    }
}
