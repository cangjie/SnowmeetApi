using System;
using System.Collections.Generic;

namespace SnowmeetApi.Models.Rent
{
	public class RentOrderList
	{
		public class RentDeposit
		{
			public int id { get; set; }
			public string payMethod { get; set; }
			public double amount { get; set; }
			public DateTime payDate { get; set; }
		}
		public class RentRefund
		{
			public int id { get; set; }
			public int depositId { get; set; }
			public double amount { get; set; }
			public DateTime refundDate { get; set; }
		}
		public class Rental
		{
			public DateTime rentalDate { get; set; }
			public double rental { get; set; }
		}

		public class ListItem
		{
			public int id { get; set; }
			public DateTime orderDate { get; set; }
			public DateTime? payDate { get; set; }

			public RentDeposit[] deposits { get; set; }
			public double totalDeposit { get; set; }

			public Rental[] rental { get; set; }
			public double totalRental { get; set; }

			public RentRefund[] refunds { get; set; }
			public double totalRefund { get; set; }

			public string name { get; set; }
			public string cell { get; set; }
			public string staffOpenId { get; set; }
			public string staffName { get; set; }
			public string status { get; set; }
			public string dayOfWeek { get; set; }
			public int indexOfDay { get; set; }
			public string shop { get; set; }
			public string memo { get; set; } = "";
		}

		public DateTime startDate { get; set; }
		public DateTime endDate { get; set; }
		public string[] shops { get; set; }
		public int maxDepositsLength { get; set; }
		public int maxRentalLength { get; set; }
		public int maxRefundLength { get; set; }
		public List<ListItem> items { get; set; }

	}
}

