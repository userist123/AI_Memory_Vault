package com.nalo.medquery.domain.validation;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;

import com.nalo.medquery.domain.model.appointment.AppointmentRegisterData;
import com.nalo.medquery.domain.repository.IPatientRepository;

@Component
public class ValidateActivePatient implements IValidateBookAppointment {
    
    @Autowired
    private IPatientRepository patientRepository;

    public void validate(AppointmentRegisterData data){
        if (!patientRepository.existsByIdAndAtivoTrue(data.patientId())) {
            throw new IllegalArgumentException("Paciente não encontrado");
        }
    }
    
}
