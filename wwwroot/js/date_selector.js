var startDate = new Date(document.getElementById('date_from').value);
if (startDate == 'Invalid Date') {
    startDate = new Date();
    startDate.setHours(0);
    startDate.setMinutes(0);
    startDate.setSeconds(0);
    startDate.setMilliseconds(0);
}
var endDate = new Date(document.getElementById('date_to').value);
if (endDate == 'Invalid Date') {
    endDate = new Date()
    endDate.setHours(0);
    endDate.setMinutes(0);
    endDate.setSeconds(0);
    endDate.setMilliseconds(0);
}
var startDateCtl = document.getElementById('date_from');
var endDateCtl = document.getElementById('date_to');
var btn = document.getElementById('btn');
startDateCtl.value = formatDate(startDate);
endDateCtl.value = formatDate(endDate);

function setDate() {
    var startDateCtl = document.getElementById('date_from');
    var endDateCtl = document.getElementById('date_to');


    var newStartDate = new Date(startDateCtl.value);
    var newEndDate = new Date(endDateCtl.value);
    if (formatDate(newStartDate) != formatDate(startDate)) {
        newEndDate = new Date(formatDate(newStartDate));
    }
    startDateCtl.value = formatDate(newStartDate);
    endDateCtl.value = formatDate(newEndDate);

    startDate = newStartDate;
    endDate = newEndDate;
}
