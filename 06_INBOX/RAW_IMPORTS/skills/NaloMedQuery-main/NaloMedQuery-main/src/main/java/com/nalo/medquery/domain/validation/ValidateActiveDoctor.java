package com.nalo.medquery.domain.validation;

import org.springframework.stereotype.Component;
import org.springframework.beans.factory.annotation.Autowired;

import com.nalo.medquery.domain.model.appointment.AppointmentRegisterData;
import com.nalo.medquery.domain.repository.IDoctorRepository;

@Component
public class ValidateActiveDoctor implements IValidateBookAppointment {

    @Autowired
    private IDoctorRepository doctorRepository;

    public void validate(AppointmentRegisterData data){
        if (data.doctorId() != null && !doctorRepository.existsByIdAndActiveTrue(data.doctorId())) {
            throw new IllegalArgumentException("Médico não encontrado ou inativo");
        }
    }
}
