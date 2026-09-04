package com.nalo.medquery.domain.validation;

import org.springframework.stereotype.Component;

import com.nalo.medquery.domain.model.appointment.AppointmentRegisterData;

@Component
public class LeadTimeValidator implements IValidateBookAppointment {
    public void validate(AppointmentRegisterData data){
        if (data.dia().isBefore(java.time.LocalDateTime.now().plusMinutes(30))) {
            throw new IllegalArgumentException("Agendamento deve ser feito com antecedência mínima de 30 minutos");
        }
    }
}
