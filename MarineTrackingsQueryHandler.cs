namespace Xx.Bcs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MediatR;
    using Xom.Bcs.OrderManagement.Application.Helpers;
    using Xom.Bcs.OrderManagement.Domain.Repositories;
    using Xom.Bcs.OrderManagement.Domain.Utility;
    using static Xom.Bcs.OrderManagement.Application.Queries.MarineTrackings.MarineTrackingsQueryResponse;

    public class MarineTrackingsQueryHandler : IRequestHandler<MarineTrackingsQueryRequest, MarineTrackingsQueryResponse>
    {
        private readonly MarineTrackingsQuerySharedObject sharedObject;
        private readonly IShipmentTrackingRepository shipmentTrackingRepository;
        private readonly IShipmentRepository shipmentRepository;
        private readonly IOrderRepository orderRepository;

        public MarineTrackingsQueryHandler(
            MarineTrackingsQuerySharedObject sharedObject,
            IShipmentTrackingRepository shipmentTrackingRepository,
            IShipmentRepository shipmentRepository,
            IOrderRepository orderRepository)
        {
            this.sharedObject = sharedObject;
            this.shipmentTrackingRepository = shipmentTrackingRepository;
            this.shipmentRepository = shipmentRepository;
            this.orderRepository = orderRepository;
        }

        public async Task<MarineTrackingsQueryResponse> Handle(MarineTrackingsQueryRequest request, CancellationToken cancellationToken)
        {
            var orderIdsWithPermission = await this.orderRepository.GetOrderIdsByShipToAsync(this.sharedObject.AuthorizedShipToIds, request.OrderIds);

            var shipmentTrackings = await this.shipmentTrackingRepository.GetShipmentTrackingsByBookingNumbersAsync(orderIdsWithPermission);

            if (shipmentTrackings == null)
            {
                return new MarineTrackingsQueryResponse();
            }

            var bookingNumbers = shipmentTrackings.Select(o => o.TripBookingNumber).ToList();

            var marineShipments = await this.shipmentRepository.GetShipmentsByBookingNumberAndTypeAsync(bookingNumbers, ShipmentTypeDescriptionMap.Marine);

            var truckShipments = await this.shipmentRepository.GetShipmentsByBookingNumberAndTypeAsync(bookingNumbers, ShipmentTypeDescriptionMap.Truck30);

            var shipmentTrackingByBookingNumbers = await this.shipmentTrackingRepository.GetShipmentTrackingsByBookingNumbersAsync(bookingNumbers);

            var shipmentTrackingInfo = (from booking in bookingNumbers
                                        select new
                                        {
                                            BookingNumber = booking,
                                            ShipmentTracking = new ShipmentTracking
                                            {
                                                Departure = (from e in shipmentTrackingByBookingNumbers
                                                             where booking.Equals(e.TripBookingNumber)
                                                               && new[] { "vessel_depart_origin", "vessel_load_origin" }.Contains(e.EventsActionType)
                                                             orderby e.EventsActionType // priority is Event vessel_depart_origin
                                                             select new Departure
                                                             {
                                                                 EventsName = e.EventsName,
                                                                 TripEventsLocationName = e.TripEventsLocationName,
                                                                 TripEventsActualTime = e.TripEventsActualTime,
                                                                 TripEventsPredictedTime = e.TripEventsPredictedTime,
                                                                 TripEventsCarrierPlannedTime = e.TripEventsCarrierPlannedTime,
                                                                 LastProcessedTime = e.LastProcessedTime,
                                                             }).FirstOrDefault(),
                                                Arrival = (from f in shipmentTrackingByBookingNumbers
                                                           where booking.Equals(f.TripBookingNumber)
                                                            && new[] { "vessel_arrive_destination", "vessel_discharge_destination" }
                                                            .Contains(f.EventsActionType)
                                                           orderby f.EventsActionType // priority is Event vessel_arrive_destination
                                                           select new Arrival
                                                           {
                                                               EventsName = f.EventsName,
                                                               TripStatusLocationName = f.TripEventsLocationName,
                                                               TripEventsActualTime = f.TripEventsActualTime,
                                                               TripEventsPredictedTime = f.TripEventsPredictedTime,
                                                               TripEventsCarrierPlannedTime = f.TripEventsCarrierPlannedTime,
                                                               LastProcessedTime = f.LastProcessedTime,
                                                           }).FirstOrDefault(),
                                                MostRecentActivity = !request.RecentActivity ? null :
                                                                    new MostRecentActivity()
                                                                    {
                                                                        Preparation = (from h in shipmentTrackingByBookingNumbers
                                                                                       where booking.Equals(h.TripBookingNumber)
                                                                                       && "export_drayage_arrive".Equals(h.EventsActionType)
                                                                                       select new Preparation
                                                                                       {
                                                                                           EventsName = h.EventsName,
                                                                                           TripEventsLocationName = h.TripEventsLocationName,
                                                                                           TripEventsActualTime = h.TripEventsActualTime,
                                                                                           TripEventsPredictedTime = h.TripEventsPredictedTime,
                                                                                           TripEventsCarrierPlannedTime = h.TripEventsCarrierPlannedTime,
                                                                                           LastProcessedTime = h.LastProcessedTime,
                                                                                       }).FirstOrDefault(),
                                                                        InTransit = (from i in shipmentTrackingByBookingNumbers
                                                                                     where booking.Equals(i.TripBookingNumber)
                                                                                     && !new[]
                                                                                         {
                                                                                        "vessel_depart_origin",
                                                                                        "vessel_load_origin",
                                                                                        "vessel_arrive_destination",
                                                                                        "vessel_discharge_destination",
                                                                                        "export_drayage_arrive",
                                                                                         }.Contains(i.EventsActionType)
                                                                                     && i.TripEventsActualTime != null
                                                                                     select new InTransit
                                                                                     {
                                                                                         EventsName = i.EventsName,
                                                                                         TripEventsLocationName = i.TripEventsLocationName,
                                                                                         TripEventsActualTime = i.TripEventsActualTime,
                                                                                         TripEventsPredictedTime = i.TripEventsPredictedTime,
                                                                                         TripEventsCarrierPlannedTime = i.TripEventsCarrierPlannedTime,
                                                                                         LastProcessedTime = i.LastProcessedTime,
                                                                                     }).OrderByDescending(o => o.TripEventsActualTime).FirstOrDefault(),
                                                                    },
                                            },
                                        }).ToList();

            var bookingInfo = (from a in shipmentTrackings
                               select new
                               {
                                   OrderId = a.SalesOrderId,
                                   BookingNumber = new BookingNumbers
                                   {
                                       BookingNumber = a.TripBookingNumber,
                                       MarineShipmentInfo = marineShipments == null ? null : (from c in marineShipments
                                                                                              where a.TripBookingNumber.Equals(c.BookingNumber)
                                                                                              select new ShipmentInfo
                                                                                              {
                                                                                                  ExecutingCarrierName = c.ExecutingCarrierName,
                                                                                                  VesselName = c.VesselName,
                                                                                                  VoyageNumber = c.VoyageNumber,
                                                                                                  ActualDateForShipmentStart = c.ActualDateForLoadingStart,
                                                                                                  ActualDateForShipmentEnd = c.ActualDateForLoadingEnd,
                                                                                                  PlannedDateForShipmentStart = c.PlannedDateForLoadingStart,
                                                                                                  PlannedDateForShipmentEnd = c.PlannedDateForShipmentEnd,
                                                                                              }).ToList(),
                                       BookingEarliestEtd = (from b in shipmentTrackingInfo
                                                             where b.BookingNumber == a.TripBookingNumber
                                                             select b.ShipmentTracking.Departure?.TripEventsActualTime
                                                             ?? b.ShipmentTracking.Departure?.TripEventsPredictedTime
                                                             ?? b.ShipmentTracking.Departure?.TripEventsCarrierPlannedTime).FirstOrDefault(),

                                       BookingEarliestEta = (from b in shipmentTrackingInfo
                                                             where b.BookingNumber == a.TripBookingNumber
                                                             select b.ShipmentTracking.Arrival?.TripEventsActualTime
                                                             ?? b.ShipmentTracking.Arrival?.TripEventsPredictedTime
                                                             ?? b.ShipmentTracking.Arrival?.TripEventsCarrierPlannedTime).FirstOrDefault(),

                                       TruckShipments = truckShipments == null ? null : (from c in truckShipments
                                                                                         where a.TripBookingNumber.Equals(c.BookingNumber)
                                                                                         select new TruckShipment
                                                                                         {
                                                                                             ShipmentNumber = c.ShipmentNumber,
                                                                                             ContainerId = c.ContainerId,
                                                                                             ShipmentTypeDescription = c.ShipmentTypeDescription,
                                                                                         }).ToList(),
                                       ShipmentTracking = shipmentTrackingInfo?.Where(o => a.TripBookingNumber.Equals(o.BookingNumber))?.Select(o => o.ShipmentTracking)?.FirstOrDefault(),
                                   },
                               }).ToList();

            var marineTrackings = (from orderId in orderIdsWithPermission
                                   select new MarineTracking
                                   {
                                       SalesOrderId = orderId,
                                       SalesOrderEarliestEtd = bookingInfo
                                               ?.Where(o => orderId.Equals(o.OrderId))
                                               .Select(o => o.BookingNumber)
                                               .OrderBy(o => o.BookingEarliestEtd)
                                               .FirstOrDefault()
                                               ?.BookingEarliestEtd,
                                       SalesOrderEarliestEta = bookingInfo
                                               ?.Where(o => orderId.Equals(o.OrderId))
                                               .Select(o => o.BookingNumber)
                                               .OrderBy(o => o.BookingEarliestEta)
                                               .FirstOrDefault()
                                               ?.BookingEarliestEta,
                                       BookingNumbers = bookingInfo?.Where(o => orderId.Equals(o.OrderId))?.Select(o => o.BookingNumber)?.ToList(),
                                   }).ToList();

            var result = new MarineTrackingsQueryResponse
            {
                MarineTrackings = marineTrackings,
            };
            return result;
        }
    }
}
