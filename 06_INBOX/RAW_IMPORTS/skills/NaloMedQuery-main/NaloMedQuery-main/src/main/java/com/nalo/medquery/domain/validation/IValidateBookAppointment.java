package com.nalo.medquery.domain.validation;

import com.nalo.medquery.domain.model.appointment.AppointmentRegisterData;

public interface IValidateBookAppointment {

    void validate(AppointmentRegisterData data);

}
