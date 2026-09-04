package com.nalo.medquery.domain.validation;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;

import com.nalo.medquery.domain.model.appointment.AppointmentRegisterData;
import com.nalo.medquery.domain.repository.IAppointmentRepository;

@Component
public class ValidateDoctorNonDuplicateBook implements IValidateBookAppointment {
    
    @Autowired
    private IAppointmentRepository appointmentRepository;
    
    public void validate(AppointmentRegisterData data){
        var doctorWithOneAppointmentsAtSameTime = appointmentRepository.existsByDoctor_IdAndDate(data.doctorId(), data.dia());
        if (doctorWithOneAppointmentsAtSameTime) {
            throw new IllegalArgumentException("Médico com mais de um agendamento no mesmo horário");
        }
    }
}
