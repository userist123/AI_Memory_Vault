package com.nalo.medquery.domain.validation;

import com.nalo.medquery.domain.repository.IAppointmentRepository;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;

import com.nalo.medquery.domain.model.appointment.AppointmentRegisterData;


@Component
public class ValidateNonDuplicatePatient implements IValidateBookAppointment {
   
    @Autowired
    private IAppointmentRepository appointmentRepository;

    public void validate(AppointmentRegisterData    data){
        var patientWithOneAppointmentsAtSameTime = appointmentRepository.existsByPatient_IdAndDate(data.patientId(), data.dia());
        if (patientWithOneAppointmentsAtSameTime) {
            throw new IllegalArgumentException("Paciente com mais de um agendamento no mesmo horário");
        }
    }
    
}
