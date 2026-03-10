namespace VuaRauAPI
{
    public class PriceData
    {
        // Đây là cột giá (DGXuat trong ảnh của bạn)
        public float Gia { get; set; }
    }

    public class PriceForecast
    {
        // Đây là mảng chứa các mức giá dự báo trả về
        public float[] Forecast { get; set; }
    }
}
