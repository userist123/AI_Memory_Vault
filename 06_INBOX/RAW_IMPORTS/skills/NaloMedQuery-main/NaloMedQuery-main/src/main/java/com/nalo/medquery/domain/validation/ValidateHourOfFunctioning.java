package com.nalo.medquery.domain.validation;

import org.springframework.stereotype.Component;

import com.nalo.medquery.domain.model.appointment.AppointmentRegisterData;

@Component
public class ValidateHourOfFunctioning implements IValidateBookAppointment {

    public void validate(AppointmentRegisterData data){
        
        if (data.dia().getDayOfWeek() == java.time.DayOfWeek.SATURDAY || data.dia().getDayOfWeek() == java.time.DayOfWeek.SUNDAY) {
            throw new IllegalArgumentException("Fora do horário de funcionamento");
        }

        if (data.dia().getHour() < 7 || data.dia().getHour() > 18) {
            throw new IllegalArgumentException("Fora do horário de funcionamento");
        }
       
    }   

}
